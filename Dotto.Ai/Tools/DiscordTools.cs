using System.ComponentModel;
using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Sandbox;
using Dotto.Application.Abstractions.Factories;
using Dotto.Infrastructure.Downloader.Contracts.Models;

namespace Dotto.Ai.Tools;

public class DiscordTools(
    ContextAccessor accessor,
    IDownloaderServiceFactory downloaderFactory,
    ISandbox sandbox)
{
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
        var downloaders = downloaderFactory.CreateDownloaderService(query);
        IList<DownloadedMedia> downloaded = [];

        foreach (var downloader in downloaders)
        {
            try
            {
                var results = await downloader.Download(query, new DownloadOptions
                {
                    AudioOnly = audioOnly,
                    MaxFilesize = 100L * 1024 * 1024
                }, ct);

                if (results.Count > 0)
                {
                    downloaded = results;
                    break;
                }
            }
            catch { }
        }

        if (downloaded.Count == 0)
            return "Failed to download media from the provided URL.";

        var container = await sandbox.EnsureStartedAsync(ct);

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

                // The server owns path resolution and collision handling. The
                // bytes are the only thing that crosses the boundary.
                var sandboxPath = await container.PutFileAsync(
                    $"/work/downloads/{fileName}",
                    media.Video,
                    length,
                    dedupe: true,
                    ct);

                sandboxPaths.Add(sandboxPath);
            }
        }
        finally
        {
            foreach (var media in downloaded)
                await media.DisposeAsync();
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
            return "Invalid file path: path must be a sandbox-relative /work/... or /storage/... path and must not contain '..'.";
        }

        var container = await sandbox.EnsureStartedAsync(ct);

        SandboxFile file;
        try
        {
            file = await container.GetFileAsync(sandboxPath, ct);
        }
        catch (SandboxHttpException ex) when (ex.StatusCode == 404)
        {
            return $"File not found: `{sandboxPath}`. Check the path and try again.";
        }

        var fileName = Path.GetFileName(sandboxPath);
        _context.Attachments.Add(new AgentAttachment(file.Stream, fileName, file.Length));
        return $"Enqueued `{fileName}` for upload.";
    }
}
