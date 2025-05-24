using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dotto.Application.InternalServices.DownloaderService;
using Dotto.Application.InternalServices.DownloaderService.Metadata;
using Dotto.Common;
using Dotto.Infrastructure.Downloader.Settings;
using YoutubeDLSharp.Options;

namespace Dotto.Infrastructure.Downloader;

public class YtdlDownloaderService(DownloaderSettings settings) : IDownloaderService
{
	/// <summary>
    /// Downloads a videos then returns a list of DownloadedMedia with the video contents, metadata and picked formats
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">Video filesize exceeded upload limit</exception>
    /// <exception cref="ApplicationException">yt-dlp exited with a non-zero exitcode</exception>
    public async Task<IList<DownloadedMedia>> Download(Uri uri, DownloadOptions options, CancellationToken ct = default)
    {
	    var tempPath = string.IsNullOrWhiteSpace(settings.TempPath)
		    ? Path.Combine(Path.GetTempPath(), "dotto_dl")
		    : settings.TempPath;
	    
	    var dir = Directory.CreateDirectory(tempPath);
	    
	    var videos = await DownloadAllVideos(uri, dir, options, ct);
	    
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
		    StandardOutputEncoding = Encoding.ASCII,
		    StandardErrorEncoding = Encoding.ASCII,
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
		    DumpJson = true,
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

		var tasks = new List<Task>();
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
		    var format = PickFormat(metadata, options);
		    var currentIndex = index;
		    if (!format.HasValue)
		    {
			    throw new InvalidOperationException($"failed to pick format for video #{index}");
		    }

		    // i'm worried launching multiple concurrent yt-dlp's may hit ratelimits,
		    // but fuck it we ball
		    var task = DownloadVideo(uri, dir.FullName, line, index.ToString(), format.Value.formatString, ct)
			    .ContinueWith(task =>
			    {
					videos.Add(new DownloadedMedia
					{
						Video = task.Result,
						Number = currentIndex,
						Metadata = metadata,
						AudioFormat = format.Value.audioFormat,
						VideoFormat = format.Value.videoFormat
					});
			    }, ct);
		    
		    tasks.Add(task);
		    
		    // otherwise it may throw an ObjectDisposedException lol i hate the process api
		    if (exitTask.IsCompleted) break;
	    }

	    await Task.WhenAll(tasks);
	    
		var exitCode = await exitTask;
	    if (exitCode != 0 && exitCode != 101)
	    {	
		    throw new ApplicationException($"yt-dlp exited with non-zero code ({exitCode})", new Exception(error));
	    }

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
    
    /// <summary>
	/// Given a list of formats, tries to pick one (merged) or multiple (audio+video) that would be the best,
	/// taking into account their resolutions, video codecs, filesizes and the upload limit.
	/// Difference against `TryPickOptimalFormat` is that this tries to pick among videos with known-supported vcodecs first
	/// </summary>
	private (FormatData? videoFormat, FormatData? audioFormat, string formatString)? PickFormat(DownloadedMediaMetadata metadata, DownloadOptions options)
	{
		if (metadata.Formats.IsNullOrEmpty())
		{
			// fallback for when there are no format selections (like instagram reels)
			return new()
			{
				audioFormat = new(),
				videoFormat = new() { Resolution = metadata.Resolution, VideoCodec = metadata.VideoCodec },
				formatString = metadata.FormatId! // TODO: is FormatID *actually* nullable? feels like it shouldn't be
			};
		}
		
		var audioFormats = metadata.Formats
			.Where(f => f.VideoCodec == "none" && f.AudioCodec != "none")
			.OrderBy(f => f.FileSize ?? f.ApproximateFileSize ?? options.MaxFilesize)
			.ToList();
		
		var videoFormats = GetEligibleVideos(metadata.Formats, false)
			.OrderBy(f => f.FileSize ?? f.ApproximateFileSize ?? options.MaxFilesize)
			.ToList();
		
		if (metadata.Extractor == "Instagram")
		{
			// reels are cooking some diabolical shit
			// formats that identify themselves as vp9 are not actually source quality
			var unknownFormat = metadata.Formats
				.Where(f => f.VideoCodec is "unknown" or null)
				.ToList();

			if (unknownFormat.Count > 0)
			{
				videoFormats = unknownFormat;
			}
		}

		var bestFormat = TryPickOptimalFormat(audioFormats, videoFormats, options);

		if (bestFormat.HasValue)
		{
			return bestFormat.Value;
		}
		
		// failed to pick a format, see if there are any formats with unknown vcodec we could add to the pool...
		videoFormats = GetEligibleVideos(metadata.Formats, true)
			.OrderBy(f => f.FileSize ?? f.ApproximateFileSize ?? options.MaxFilesize)
			.ToList();
		
		return TryPickOptimalFormat(audioFormats, videoFormats, options);
	}

	private static readonly List<(Regex, double)> FormatQualityRatio =
	[
		// fuck h264
		(new("^(avc.*|h264.*)"), 60),
		
		// h265 is baseline quality
		(new("^(avc.*|h264.*)"), 100),
		
		// about the same for vp9, but vp9 gets a boost for being more supported :^)
		(new("^(vp0?9.*)"), 110),
	];
	
	/// <summary>
	/// Given a list of audio and video formats, tries to pick one (merged) or multiple (audio+video) that would be the best
	/// </summary>
	private (FormatData? videoFormat, FormatData? audioFormat, string formatString)?
		TryPickOptimalFormat(IList<FormatData> audioFormats, IList<FormatData> videoFormats, DownloadOptions options)
	{
		(FormatData? videoFormat, FormatData? audioFormat)? choice = null;
		var bestScore = long.MinValue;

		foreach (var vformat in videoFormats)
		{
			var isMerged = vformat.AudioCodec != "none";
			
			var vsize = vformat.FileSize ?? vformat.ApproximateFileSize ?? 0;
			if (vsize > options.MaxFilesize) break; // lists are sorted by size; break here knowing the remaining formats are even bigger

			var bytesLeft = options.MaxFilesize - vsize;

			if (isMerged)
			{
				// premerged format, don't need to pick audio separately
				var leftover = bytesLeft - vsize;
				var score = GetFormatScore(vformat, leftover);
				
				if (score <= bestScore) continue;
				
				// new optimal combination found
				bestScore = score;
				choice = (vformat, null);
			}
			else
			{
				// still need to pick audio
				foreach (var aformat in audioFormats)
				{
					var asize = aformat.FileSize ?? aformat.ApproximateFileSize ?? 0;
					if (asize > bytesLeft) break; // lists are sorted by size; break here knowing the remaining formats are even bigger

					if (!IsAllowedCombination(vformat, aformat)) continue;
					
					var leftover = bytesLeft - asize;
					var score = GetFormatScore(vformat, leftover);
					
					if (score <= bestScore) continue;
					
					// new optimal combination found
					bestScore = score;
					choice = (vformat, aformat);
				}
			}	
		}

		if (!choice.HasValue) return null;
		
		var fmt = choice.Value;
		var fmtString = fmt is { videoFormat: not null, audioFormat: not null }
			? $"{fmt.videoFormat.FormatId}+{fmt.audioFormat.FormatId}"
			: $"{(fmt.videoFormat ?? fmt.audioFormat)!.FormatId}";

		return (fmt.videoFormat, fmt.audioFormat, fmtString);
	}

	/// <summary>
	/// Scores a selected format, so it can be prioritized against others
	/// (i.e. better resolutions should trump worse ones, better codecs should beat worse ones,
	/// videos closer to the upload limit are preferred)
	/// </summary>
	private long GetFormatScore(FormatData format, long leftover)
	{
		if (leftover < 0 || format.VideoCodec == null)
		{
			return -long.MaxValue;
		}
		
		// higher res basically always beats codec choice
		var resScore = format.Width * format.Height / 1e6
		               ?? 1.0d;
		
		var matchedScoreMult = FormatQualityRatio.FirstOrDefault(data => data.Item1.IsMatch(format.VideoCodec));

		var scoreMult = matchedScoreMult != default
			? matchedScoreMult.Item2
			: 1.0d;
		
		// less leftover, higher score
		var score = -leftover;
		
		// divide so the mult interacts with negative numbers correctly
		score = (long)(score / scoreMult / resScore);
		
		return score;
	}

	/// <summary>
	/// Some combinations can't be combined due to container restrictions (ie: webm video and m4a will result in an unembeddable MKV)
	/// </summary>
	private bool IsAllowedCombination(FormatData vformat, FormatData aformat)
	{
		// m4a audio can't be embedded in webm containers
		if (vformat.Extension != "mp4" && aformat.Extension == "m4a")
		{
			return false;
		}

		return true;
	}
	
	private static readonly Regex FormatRegex = new("^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)");

	private IList<FormatData> GetEligibleVideos(IList<FormatData> formats, bool allowUnknownVcodec)
	{
		return formats
			.Where(f => (allowUnknownVcodec && f.VideoCodec is "unknown" or null)
					    || (f.VideoCodec != null && FormatRegex.IsMatch(f.VideoCodec)))
			.ToList();
	}
}