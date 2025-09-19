using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Dotto.Common;
using Dotto.Infrastructure.Downloader.Contracts.Interfaces;
using Dotto.Infrastructure.Downloader.Contracts.Models;
using Dotto.Infrastructure.Downloader.Contracts.Models.Metadata;
using Dotto.Infrastructure.Downloader.Settings;
using YoutubeDLSharp.Options;

namespace Dotto.Infrastructure.Downloader.YtdlDownloader;

public class YtdlDownloaderService(DownloaderSettings settings) : IDownloaderService
{
	private readonly YtdlFormatParser _ytdlFormatParser = new();
	
	/// <summary>
    /// Downloads a videos then returns a list of DownloadedMedia with the video contents, metadata and picked formats
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">Video filesize exceeded upload limit</exception>
    /// <exception cref="ApplicationException">yt-dlp exited with a non-zero exitcode</exception>
    public async Task<IList<DownloadedMedia>> Download(Uri uri, DownloadOptions options, CancellationToken cancellationToken = default)
    {
	    var tempPath = string.IsNullOrWhiteSpace(settings.TempPath)
		    ? Path.Combine(Path.GetTempPath(), "dotto_dl")
		    : settings.TempPath;
	    
	    var dir = Directory.CreateDirectory(tempPath);
	    
	    var videos = await DownloadAllVideos(uri, dir, options, cancellationToken);
	    
        return videos;
    }

    private Process StartYtdlp(string url, OptionSet options)
    { 
	    var process = new Process();
	    var processStartInfo = new ProcessStartInfo
	    {
		    FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? "yt-dlp.exe" // TODO: This kinda sucks. Configurable path?
				: "yt-dlp",
		    Arguments = OptionsToArgString(url, options),
		    CreateNoWindow = true,
		    UseShellExecute = false,
		    RedirectStandardOutput = true,
		    RedirectStandardError = true,
		    RedirectStandardInput = true,
		    StandardOutputEncoding = Encoding.UTF8,
		    StandardErrorEncoding = Encoding.UTF8,
		    StandardInputEncoding = Encoding.ASCII,
	    };
	    
		process.EnableRaisingEvents = true;
		process.StartInfo = processStartInfo;

		return process;
    }

    /// <summary>
    /// Grabs video(s) information as a JSON, picks a format for each, then downloads them into MemoryStreams
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">Video filesize exceeded upload limit</exception>
    /// <exception cref="ApplicationException">yt-dlp exited with a non-zero exitcode</exception>
    private async Task<IList<DownloadedMedia>> DownloadAllVideos(Uri uri, DirectoryInfo dir,
	    DownloadOptions options, CancellationToken ct = default)
    {
	    // Grabs information about the video(s) as JSON
	    var opts = new OptionSet
	    {
		    Quiet = true,
		    NoWarnings = true,
		    Output = "-",
		    RestrictFilenames = true,
		    DumpJson = true,
		    CompatOptions = "manifest-filesize-approx",
		    Simulate = true,
		    MaxDownloads = (int?)options.MaxDownloads
	    };
	    
	    if (uri.Host.Contains("tiktok"))
	    {
		    // workaround for tiktok not extracting: https://github.com/yt-dlp/yt-dlp/issues/9506#issuecomment-2053987537
		    opts.ExtractorArgs = "tiktok:api_hostname=api16-normal-c-useast1a.tiktokv.com;app_info=7355728856979392262";
	    }
		        
	    var process = StartYtdlp(uri.AbsoluteUri, opts);
		process.Start();
		
		var exitTask = SetupExit(process, ct);
		
		var videos = new List<DownloadedMedia>();
		var index = 0;

		var downloadTasks = new List<Task<DownloadedMedia>>();
		string? error = null;
		
		while (await process.StandardError.ReadLineAsync(ct) is { } line)
		{
			DownloadedMediaMetadata metadata;
			try
			{
				metadata = JsonSerializer.Deserialize<DownloadedMediaMetadata>(line)
					?? throw new InvalidOperationException("yt-dlp outputted JSON that couldn't be serialized to metadata!?");
			}
			catch (JsonException)
			{
				// if we received a non-json in stderr, it's likely an error message,
				// which we should store once yt-dlp exits with a non-zero code
				error = line + await process.StandardError.ReadToEndAsync(ct);
				break;
			}

		    index++;
		    var format = _ytdlFormatParser.PickFormat(metadata, options);
		    
		    if (format == null)
			    throw new ApplicationException($"failed to pick format for video #{index}");

		    var currentIndex = index;
		    
		    // i'm worried launching multiple concurrent yt-dlp's may hit ratelimits,
		    // but fuck it we ball
		    var task = DownloadVideo(uri, dir.FullName, line, index.ToString(), format.FormatString, ct)
			    .ContinueWith(task => new DownloadedMedia
				{
					Video = task.Result,
					FileSize = task.Result.Length,
					Number = currentIndex,
					Metadata = metadata,
					AudioFormat = format.AudioFormat,
					VideoFormat = format.VideoFormat
				}, ct);
		    
		    downloadTasks.Add(task);
		    
		    // otherwise it may throw an ObjectDisposedException lol i hate the process api
		    if (exitTask.IsCompleted) break;
	    }

	    var downloadedMedia = await Task.WhenAll(downloadTasks);
	    videos.AddRange(downloadedMedia);
	    
		var exitCode = await exitTask;
		
	    if (exitCode != 0 && exitCode != 101)
		    throw new ApplicationException($"yt-dlp exited with non-zero code ({exitCode})", new Exception(error));

	    return videos;
    }

    /// <summary>
    /// Downloads a video in the URL given a format spec and index
    /// </summary>
    /// <param name="uri">URL to the video or playlist</param>
    /// <param name="dirPath">Directory to save temporary video files to</param>
    /// <param name="infoJson">Info JSON to pass to yt-dlp (see: --load-info-json)</param>
    /// <param name="index">Index of the video in a playlist</param>
    /// <param name="format">Format spec to download (see: --format)</param>
    /// <param name="ct"></param>
    /// <returns>MemoryStream of the downloaded video</returns>
    /// <exception cref="IndexOutOfRangeException">Video filesize exceeded upload limit</exception>
    /// <exception cref="ApplicationException">yt-dlp exited with a non-zero exitcode</exception>
    private async Task<Stream> DownloadVideo(Uri uri,
	    string dirPath, string infoJson,
	    string index, string format, CancellationToken ct)
    {
	    var opts = new OptionSet
	    {
		    Quiet = true,
		    NoWarnings = true,
		    Output = Path.Combine(dirPath, Guid.NewGuid().ToString("N") + "%(id)s.%(ext)s"),
		    LoadInfoJson = "-", // load infojson from stdin
		    Print = "after_move:filepath", // emit the full downloaded path to stdout
		    
		    Format = format,
		    PlaylistItems = index,
		    MaxDownloads = 1 // format selection only applies to 1 file 
	    };
	    
	    if (uri.Host.Contains("tiktok"))
	    {
		    // workaround for tiktok not extracting: https://github.com/yt-dlp/yt-dlp/issues/9506#issuecomment-2053987537
		    opts.ExtractorArgs = "tiktok:api_hostname=api16-normal-c-useast1a.tiktokv.com;app_info=7355728856979392262";
	    }
	    
	    var process = StartYtdlp(uri.AbsoluteUri, opts);
	    var exitTask = SetupExit(process, ct);

        process.Start();
        await process.StandardInput.WriteAsync(infoJson);
        process.StandardInput.Close();
        

	    var filepath = await process.StandardOutput.ReadLineAsync(ct);
	    var error = await process.StandardError.ReadToEndAsync(ct);
	    
	    var exitCode = await exitTask;
	    
	    // 101 means one or more downloads were aborted by --max-downloads, which is acceptable
	    if (exitCode != 0 && exitCode != 101)
	    {	
		    throw new ApplicationException($"yt-dlp exited with non-zero code ({exitCode})", new Exception(error));
	    }

	    if (filepath.IsNullOrEmpty())
	    {
		    throw new ApplicationException("yt-dlp didn't return filepath to the downloaded video");
	    }
	    
	    return new FileStream(filepath,
		    FileMode.Open, FileAccess.Read, FileShare.Delete,
		    16384,
		    FileOptions.DeleteOnClose);
    }
    
    /// <summary>
    /// Hooks up the process' exit to a task so the exitcode can be obtained, and makes the cancellation token terminate the process
    /// </summary>
    private Task<int> SetupExit(Process process, CancellationToken ct)
    {
	    // the process api is really unergonomic; trying to get process.ExitCode just fucking throws. thanks c#!!!
	    var ecTcs = new TaskCompletionSource<int>();
	    
	    // on exit, set ecTcs' result to the exitcode value, since that looks like the only way to get it
	    process.Exited += (_, _) =>
	    {
		    ecTcs.SetResult(process.ExitCode);
		    process.Dispose();
	    };
	    
	    ct.Register(() =>
	    {
		    if (!process.HasExited)
		    {
			    process.Kill();
		    }
	    });

	    return ecTcs.Task;
    }
    
    private static string OptionsToArgString(string url, OptionSet options)
		=> options + $" -- \"{url}\"";
}