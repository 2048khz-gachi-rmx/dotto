using System.ComponentModel;
using Dotto.Ai.Abstractions;
using Dotto.Ai.Internal;
using Dotto.Ai.Models;
using Dotto.Application.Abstractions.Factories;
using Dotto.Infrastructure.Downloader.Contracts.Models;

namespace Dotto.Ai.Tools;

public class DiscordTools(
    ContextAccessor accessor,
    IDownloaderServiceFactory downloaderFactory,
    ISandbox sandbox)
{
    private readonly ChatAssistantContext _context = accessor.Get<ChatAssistantContext>();
    private readonly string _sessionDir = sandbox.Metadata.HostSessionDir;

    [Tool(ToolAttribute.ToolTypeFlag.UserAssistant)]
    [Description("Download media using yt-dlp. Saves the file to the sandbox at /work/downloads/... for further processing. Use for Instagram, TikTok, YouTube, Twitter, etc.")]
    public async Task<string> DownloadMedia(
        [Description("Query: either a URL or yt-dlp compatible query (in the format: \"prefix:search query\". " +
                     "Supported prefixes: `ytsearch` for YouTube, `scsearch` for SoundCloud.")]
        string query,
        bool audioOnly = false)
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
                });

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

        var downloadsDir = Path.Combine(_sessionDir, "downloads");
        Directory.CreateDirectory(downloadsDir);

        var sandboxPaths = new List<string>();
        try
        {
            foreach (var media in downloaded)
            {
                var fileName = media.GetFileName();
                var filePath = Path.Combine(downloadsDir, fileName);

                // Avoid clobbering existing files (e.g. duplicate titles in a playlist)
                if (File.Exists(filePath))
                {
                    var dir = Path.GetDirectoryName(filePath)!;
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var i = 1;
                    while (File.Exists(filePath))
                        filePath = Path.Combine(dir, $"{nameWithoutExt}_{i++}{ext}");
                }

                await using var fileStream = File.Create(filePath);
                await media.Video.CopyToAsync(fileStream);

                sandboxPaths.Add($"/work/downloads/{Path.GetFileName(filePath)}");
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
    [Description("Upload a file from the sandbox to Discord as an attachment. The path is sandbox-relative and starts with /work (e.g. \"/work/outputs/result.mp4\").")]
    public async Task<string> UploadFile(string sandboxPath)
    {
        if (string.IsNullOrWhiteSpace(sandboxPath) ||
            !sandboxPath.StartsWith("/work/", StringComparison.Ordinal) ||
            sandboxPath.Contains(".."))
        {
            return "Invalid file path: path must be a sandbox-relative /work/... path and must not contain '..'.";
        }

        var relative = sandboxPath["/work/".Length..].Replace('\\', '/');
        var fullPath = Path.Combine(_sessionDir, relative);
        if (!File.Exists(fullPath))
            return $"File not found: `{sandboxPath}`. Check the path and try again.";

        var fileInfo = new FileInfo(fullPath);
        var fileName = fileInfo.Name;

        var fileStream = new FileStream(fullPath,
            FileMode.Open, FileAccess.Read, FileShare.Delete,
            16384,
            FileOptions.DeleteOnClose);

        _context.Attachments.Add(new(fileStream, fileName));
        return $"Enqueued `{fileName}` for upload.";
    }
}
