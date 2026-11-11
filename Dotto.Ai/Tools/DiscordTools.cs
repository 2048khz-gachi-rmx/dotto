using System.ComponentModel;
using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Sandbox;
using Dotto.Application.Abstractions.Factories;
using Dotto.Application.Abstractions.MediaProcessing;
using Dotto.Application.Models;
using Dotto.Infrastructure.Downloader.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Dotto.Ai.Tools;

public class DiscordTools(
    ContextAccessor accessor,
    IDownloaderServiceFactory downloaderFactory,
    IMediaDownloader mediaDownloader,
    ISandbox sandbox,
    ILogger<DiscordTools>? logger = null)
{
    private const long MaxDownloadBytes = 100L * 1024 * 1024;

    private readonly ChatAssistantContext _context = accessor.Get<ChatAssistantContext>();

    [Tool(ToolAttribute.ToolTypeFlag.UserAssistant)]
    [Description("Download media using yt-dlp. Saves the file to the sandbox at /work/downloads/... for further processing. Use for Instagram, TikTok, YouTube, Twitter, etc.")]
    public async Task<string> DownloadMedia(
        [Description("Query: either a URL or yt-dlp compatible query (in the format: \"prefix:search query\". " +
                     "Supported prefixes: `ytsearch` for YouTube, `scsearch` for SoundCloud.")]
        string query,
        bool audioOnly = false,
        CancellationToken ct = default)
    {
        var options = new DownloadOptions
        {
            AudioOnly = audioOnly,
            MaxFilesize = MaxDownloadBytes
        };

        IList<DownloadedMedia> downloaded = [];
        var fatalErrors = new List<string>();

        if (Uri.TryCreate(query, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                var result = await mediaDownloader.DownloadMediaFromUrl(uri, options, ct);
                downloaded = result.Media;
                fatalErrors.AddRange(result.Errors
                    .Where(e => e.ErrorCode == MediaErrorCode.DownloaderError)
                    .Select(e => e.Message));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Anything the downloader lets escape must not abort the agent run.
                logger?.LogWarning(ex, "Media download failed for {Url}", uri);
                fatalErrors.Add(ex.Message);
            }
        }
        else
        {
            foreach (var downloader in downloaderFactory.CreateDownloaderService(query))
            {
                try
                {
                    var results = await downloader.Download(query, options, ct);

                    if (results.Count > 0)
                    {
                        downloaded = results;
                        break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger?.LogWarning(ex,
                        "Downloader {Downloader} failed for query {Query}",
                        downloader.GetType().Name, query);
                    fatalErrors.Add(ex.Message);
                }
            }
        }

        if (downloaded.Count == 0)
        {
            const string failure = "Failed to download media from the provided URL.";
            if (fatalErrors.Count == 0)
                throw new ToolFailureException(failure);

            throw new ToolFailureException(
                $"{failure}\n{string.Join("\n", fatalErrors.Distinct().Select(e => $"- {e}"))}");
        }

        ISandboxContainer container;
        try
        {
            container = await sandbox.EnsureStartedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await DisposeAllAsync(downloaded);
            logger?.LogError(ex, "Sandbox failed to start for DownloadMedia");
            throw new ToolFailureException(SandboxToolMessages.StartFailed);
        }

        var sandboxPaths = new List<string>();
        try
        {
            foreach (var media in downloaded)
            {
                var fileName = Path.GetFileName(media.GetFileName());
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "download";

                if (media.Video.CanSeek)
                    media.Video.Position = 0;

                var length = media.Video.CanSeek
                    ? media.Video.Length
                    : media.FileSize ?? throw new InvalidOperationException(
                        "Non-seekable media was returned without a known file size.");

                var sandboxPath = await container.PutFileAsync(
                    $"/work/downloads/{fileName}",
                    media.Video,
                    length,
                    dedupe: true,
                    ct);

                sandboxPaths.Add(sandboxPath);
            }
        }
        catch (SandboxHttpException ex)
        {
            logger?.LogWarning(ex, "Sandbox rejected a downloaded file");
            throw new ToolFailureException($"Failed to write the downloaded media into the sandbox: {ex.Message}");
        }
        finally
        {
            await DisposeAllAsync(downloaded);
        }

        if (sandboxPaths.Count == 1)
        {
            return $"Downloaded to `{sandboxPaths[0]}`. You can now process it with terminal commands or upload it to Discord.";
        }

        var pathsList = string.Join("\n", sandboxPaths.Select(p => $"- `{p}`"));
        return $"Downloaded {sandboxPaths.Count} files:\n{pathsList}\nYou can now process them with terminal commands or upload them to Discord.";
    }

    [Tool(ToolAttribute.ToolTypeFlag.UserAssistant)]
    [Description("Upload a file from the sandbox to Discord as an attachment. The path is sandbox-relative and starts with /work or /storage (e.g. \"/work/outputs/result.mp4\").")]
    public async Task<string> UploadFile(string sandboxPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sandboxPath) ||
            !(sandboxPath.StartsWith("/work/", StringComparison.Ordinal) ||
              sandboxPath.StartsWith("/storage/", StringComparison.Ordinal)) ||
            sandboxPath.Contains(".."))
        {
            throw new ToolFailureException(
                "Invalid file path: path must be a sandbox-relative /work/... or /storage/... path and must not contain '..'.");
        }

        ISandboxContainer container;
        try
        {
            container = await sandbox.EnsureStartedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Sandbox failed to start for UploadFile");
            throw new ToolFailureException(SandboxToolMessages.StartFailed);
        }

        SandboxFile file;
        try
        {
            file = await container.GetFileAsync(sandboxPath, ct);
        }
        catch (SandboxHttpException ex) when (ex.StatusCode == 404)
        {
            throw new ToolFailureException($"File not found: `{sandboxPath}`. Check the path and try again.");
        }
        catch (SandboxHttpException ex)
        {
            logger?.LogWarning(ex, "Sandbox rejected an upload request for {Path}", sandboxPath);
            throw new ToolFailureException($"Failed to read `{sandboxPath}` from the sandbox: {ex.Message}");
        }

        var fileName = Path.GetFileName(sandboxPath);
        _context.Attachments.Add(new AgentAttachment(file.Stream, fileName, file.Length)
        {
            SourcePath = sandboxPath
        });
        return $"Enqueued `{fileName}` for upload.";
    }

    private static async Task DisposeAllAsync(IEnumerable<DownloadedMedia> media)
    {
        foreach (var item in media)
            await item.DisposeAsync();
    }
}
