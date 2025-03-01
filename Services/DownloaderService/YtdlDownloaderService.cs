using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using YoutubeDLSharp.Options;

namespace Services.DownloaderService;

public class YtdlDownloaderService : IDownloaderService
{
    public async Task<DownloadedMedia> Download(Uri uri, bool audioOnly,
	    long uploadLimit = long.MaxValue, CancellationToken ct = default)
    {
	    // this token gets cancelled by us if downloaded video exceeds uploadLimit, which should kill the yt-dlp process
	    var forkedCt = CancellationTokenSource.CreateLinkedTokenSource(ct);
	    
	    var videoBuf = new MemoryStream();
	    var jsonBuf = new MemoryStream();
	    
	    var opts = new OptionSet()
	    {
		    Quiet = true,
		    NoWarnings = true,
		    Output = "-",
		    DumpJson = true,
		    NoSimulate = true,
		    Format = audioOnly ? GetAudioFormat() : GetVideoFormat(),
	    };

	    if (uri.Host.Contains("tiktok"))
	    {
		    // workaround for tiktok not extracting: https://github.com/yt-dlp/yt-dlp/issues/9506#issuecomment-2053987537
		    opts.ExtractorArgs = "tiktok:api_hostname=api16-normal-c-useast1a.tiktokv.com;app_info=7355728856979392262";
	    }

	    var process = StartYtdlp(uri.AbsoluteUri, opts);
	    
	    // the process api is really unergonomic
	    var ecTcs = new TaskCompletionSource<int>();
	    process.Exited += (_, _) =>
	    {
		    ecTcs.SetResult(process.ExitCode);
		    process.Dispose();
	    };
	    
	    forkedCt.Token.Register(() =>
	    {
		    if (!process.HasExited)
		    {
			    process.Kill();
		    }
	    });
	    
		process.Start();

		// read stdout (we have the uploadLimit check here so we can't just use CopyToAsync or something)
		var stdoutTask = Task.Run(async () =>
		{
			int lastRead;
			byte[] buffer = ArrayPool<byte>.Shared.Rent(81920); // matching CopyToAsync's default buffer size :)))

			try
			{
				do
				{
					lastRead = await process.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length,
						cancellationToken: forkedCt.Token);
					videoBuf.Write(buffer, 0, lastRead);

					if (videoBuf.Length > uploadLimit)
					{
						// we've read more video data than the upload limit allows; stop downloading and just bail
						forkedCt.Cancel();
						throw new IndexOutOfRangeException("video size exceeded upload limit");
					}
				} while (lastRead > 0);
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		});
		
		// read stderr
		var stderrTask = process.StandardError.BaseStream.CopyToAsync(jsonBuf, 10, forkedCt.Token);
		
	    await Task.WhenAll(stderrTask, stdoutTask);
	    var exitCode = await ecTcs.Task;
	    
	    var jsonString = Encoding.ASCII.GetString(jsonBuf.ToArray());
	    
	    if (exitCode != 0)
	    {	
		    throw new ApplicationException($"yt-dlp exited with non-zero code ({exitCode})",
			    new Exception(jsonString));
	    }

	    var metadata = JsonSerializer.Deserialize<DownloadedMediaMetadata>(jsonString);
	    
	    if (metadata == null)
	    {
		    throw new InvalidOperationException("yt-dlp outputted JSON that couldn't be serialized to metadata!?");
	    }

	    // rewind the video stream for the consumer to read
	    videoBuf.Seek(0, SeekOrigin.Begin);
	    
        return new DownloadedMedia()
        {
	        Video = videoBuf,
	        Metadata = metadata
        };
    }

    // the YoutubeDlSharp dev put fucking regex matching on stdout and it can't be disabled...
    // me when i repeatedly match regex against megabytes of binary data
    private Process StartYtdlp(string url, OptionSet options)
    { 
	    var process = new Process();
	    var processStartInfo = new ProcessStartInfo()
	    {
		    FileName = "yt-dlp.exe",
		    Arguments = ConvertToArgs(url, options),
		    CreateNoWindow = true,
		    UseShellExecute = false,
		    RedirectStandardOutput = true,
		    RedirectStandardError = true,
		    StandardOutputEncoding = Encoding.UTF8,
		    StandardErrorEncoding = Encoding.UTF8,
		    
	    };
	    
		process.EnableRaisingEvents = true;
		process.StartInfo = processStartInfo;

		return process;
    }
    
    internal string ConvertToArgs(string url, OptionSet options)
    {
      return options + $" -- \"{url}\"";
    }
    
    private string GetAudioFormat()
    {
	    // prefer audio below 10megs, but if it doesn't exist just grab whatever and try to make it work
	    return @"(ba[filesize<9800K] / ba[filesize_approx<9500K] / ba / best)";
    }
    
	private string GetVideoFormat()
    {
        // this is AWFUL
        // https://github.com/2048khz-gachi-rmx/botto_js/commit/6bb2fea944fa9220b880c37e3e5af713101a01c6#diff-43be44e39ee3915842ce24f30818e7cd118b78b93a9c238e8c95ecc2a4ecce17R57-R70
		// i wish i could do custom filtering in a proper language without having to invoke yt-dlp twice...
		
        // priority 1: select split with filesize <8m known-good format
        //             (2 combos due to filesize/filesize_approx on audio)
        // priority 2: select split with filesize_approx <8m known-good format
        //             (+2 combos for same reason)
        // priority 3: select premerged with filesize or filesize_approx <10m known-good format
        //             (+2 combos for same reason but premerged)
        // priority 4: repeat 1-2-3 except format can be missing; we exhausted best options, so whatever goes
        //			   (*2 cause we're doing the same steps but with optional vcodec filter)
        // total: 12 combinations

        return """
               ( (bv[filesize<8M][vcodec~='^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)']+ba[filesize<2M])
               / (bv[filesize<8M][vcodec~='^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)']+ba[filesize_approx<2M])
               / (bv[filesize_approx<8M][vcodec~='^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)']+ba[filesize<2M])
               / (bv[filesize_approx<8M][vcodec~='^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)']+ba[filesize_approx<2M])
               / (best[filesize<9900K][vcodec~='^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)'])
               / (best[filesize_approx<9500K][vcodec~='^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)'])
               / (bv[filesize<8M][vcodec~=?'^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)']+ba[filesize<2M])
               / (bv[filesize<8M][vcodec~=?'^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)']+ba[filesize_approx<2M])
               / (bv[filesize_approx<8M][vcodec~=?'^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)']+ba[filesize<2M])
               / (bv[filesize_approx<8M][vcodec~=?'^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)']+ba[filesize_approx<2M])
               / (best[filesize<9800K][vcodec~=?'^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)'])
               / (best[filesize_approx<9500K][vcodec~=?'^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)'])
               )
               """.Replace("\n", string.Empty).Replace("\r", string.Empty).Replace("\t", string.Empty); 
    }
}