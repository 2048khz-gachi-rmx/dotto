using System.Text.RegularExpressions;
using Dotto.Application.InternalServices.DownloaderService;
using Dotto.Application.InternalServices.DownloaderService.Metadata;
using Dotto.Common;

namespace Dotto.Infrastructure.Downloader;

internal class YtdlFormatParser
{
	/// <summary>
	/// Given a list of formats, tries to pick one (merged) or multiple (audio+video) that would be the best,
	/// taking into account their resolutions, video codecs, filesizes and the upload limit.
	/// Difference against `TryPickOptimalFormat` is that this tries to pick among videos with known-supported vcodecs first
	/// </summary>
	public (FormatData? videoFormat, FormatData? audioFormat, string formatString)? PickFormat(DownloadedMediaMetadata metadata, DownloadOptions options)
	{
		if (metadata.Formats.IsNullOrEmpty())
		{
			// fallback for when there are no format selections (like instagram reels)
			return new()
			{
				audioFormat = null,
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
		(new("^(avc.*|h264.*)"), 0.6d),
		
		// h265 is baseline quality
		(new("^(hevc.*|h265.*)"), 1d),
		
		// about the same for vp9, but vp9 gets a boost for being more supported :^)
		(new("^(vp0?9.*)"), 1.1d),
	];
	
	/// <summary>
	/// Given a list of audio and video formats, tries to pick one (merged) or multiple (audio+video) that would be the best
	/// </summary>
	public (FormatData? videoFormat, FormatData? audioFormat, string formatString)?
		TryPickOptimalFormat(IList<FormatData> audioFormats, IList<FormatData> videoFormats, DownloadOptions options)
	{
		(FormatData? videoFormat, FormatData? audioFormat)? choice = null;
		var bestScore = long.MinValue;

		foreach (var vformat in videoFormats)
		{
			var isMerged = vformat.AudioCodec != null && vformat.AudioCodec != "none";
			
			var vsize = vformat.FileSize ?? vformat.ApproximateFileSize ?? 0;
			if (vsize > options.MaxFilesize) break; // lists are sorted by size; break here knowing the remaining formats are even bigger

			var bytesLeft = options.MaxFilesize - vsize;

			if (isMerged || audioFormats.IsEmpty())
			{
				// premerged format or no audio, don't need to pick audio separately
				var score = GetFormatScore(vformat, bytesLeft);
				
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
		var resScore = (format.Width * format.Height / 1e6) ?? 1;
		
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