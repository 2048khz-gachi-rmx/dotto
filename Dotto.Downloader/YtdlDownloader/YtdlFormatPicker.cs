using System.Text.RegularExpressions;
using Dotto.Common;
using Dotto.Infrastructure.Downloader.Contracts.Models;
using Dotto.Infrastructure.Downloader.Contracts.Models.Metadata;

namespace Dotto.Infrastructure.Downloader.YtdlDownloader;

internal class YtdlFormatPicker
{
    private const double PerfectVideoRatio = 0.80;
    private const double PerfectAudioRatio = 0.20;
    
    /// <summary>
    /// Given a list of formats, tries to pick one (merged) or multiple (audio+video) that would be the best,
    /// taking into account their resolutions, video codecs, filesizes and the upload limit.
    /// Difference against `TryPickOptimalFormat` is that this tries to pick among videos with known-supported vcodecs first
    /// </summary>
    public PickedFormat? PickFormat(DownloadedMediaMetadata metadata, DownloadOptions options)
    {
        if (metadata.Formats.IsNullOrEmpty())
        {
            // fallback for when there are no format selections (like instagram reels)
            return new()
            {
                AudioFormat = null,
                VideoFormat = new() { Resolution = metadata.Resolution, VideoCodec = metadata.VideoCodec },
                FormatString = metadata.FormatId! // TODO: is FormatID *actually* nullable? feels like it shouldn't be
            };
        }
		
        var audioFormats = GetEligibleAudio(metadata.Formats)
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

        if (bestFormat != null)
            return bestFormat;
		
        // failed to pick a format, see if there are any formats with unknown vcodec we could add to the pool...
        videoFormats = GetEligibleVideos(metadata.Formats, true)
            .OrderBy(f => f.FileSize ?? f.ApproximateFileSize ?? options.MaxFilesize)
            .ToList();
		
        return TryPickOptimalFormat(audioFormats, videoFormats, options);
    }

    private static readonly List<(Regex FormatPattern, double ScoreMult)> VideoFormatQualityRatio =
    [
        // fuck h264
        (new("^(avc.*|h264.*)", RegexOptions.Compiled), 0.4d),
		
        // h265 is baseline quality
        (new("^(hevc.*|h265.*)", RegexOptions.Compiled), 1d),
		
        // about the same for vp9, but vp9 gets a boost for being more supported :^)
        (new("^(vp0?9.*)", RegexOptions.Compiled), 1.1d),
				
        // av1 is now supported by discord so lets ball
        (new("^(av0?1.*)", RegexOptions.Compiled), 1.35d),
    ];
	
    private static readonly List<(Regex FormatPattern, double ScoreMult)> AudioFormatQualityRatio =
    [
        (new("^mp4a", RegexOptions.Compiled), 1d),
		
        (new("^opus", RegexOptions.Compiled), 1.2d),
    ];
	
    /// <summary>
    /// Given a list of audio and video formats, tries to pick one (merged) or multiple (audio+video) that would be the best
    /// </summary>
    internal PickedFormat? TryPickOptimalFormat(IList<FormatData> audioFormats, IList<FormatData> videoFormats, DownloadOptions options)
    {
        (FormatData? videoFormat, FormatData? audioFormat)? choice = null;

        if (options.AudioOnly)
        {
            var audioFormat = TryPickOptimalAudioFormat(audioFormats, videoFormats, options);

            if (audioFormat != null)
                choice = (null, audioFormat);
        }
        else
            choice = TryPickOptimalVideoAudioFormat(audioFormats, videoFormats, options);

        if (!choice.HasValue) return null;
		
        var fmt = choice.Value;
        var fmtString = fmt is { videoFormat: not null, audioFormat: not null }
            ? $"{fmt.videoFormat.FormatId}+{fmt.audioFormat.FormatId}"
            : $"{(fmt.videoFormat ?? fmt.audioFormat)!.FormatId}";

        return new PickedFormat
        {
            VideoFormat = fmt.videoFormat,
            AudioFormat = fmt.audioFormat,
            FormatString = fmtString
        };
    }
        
    private (FormatData? videoFormat, FormatData? audioFormat)?
        TryPickOptimalVideoAudioFormat(IList<FormatData> audioFormats, IList<FormatData> videoFormats, DownloadOptions options)
    {
        double? bestScore = null;
        (FormatData? videoFormat, FormatData? audioFormat)? choice = null;

        if (videoFormats.IsEmpty())
        {
            // link had no videos at all; try at least returning the audio, if present
            var audioFormat = TryPickOptimalAudioFormat(audioFormats, videoFormats, options);
			
            return audioFormat != null
                ? (null, audioFormat)
                : null;
        }

        var maxVideoSize = (long)(options.MaxFilesize * PerfectVideoRatio);
        var maxAudioSize = (long)(options.MaxFilesize * PerfectAudioRatio);

        #if DEBUG
        Dictionary<(string, string), (double, double)> scores = new();
        #endif
        
        foreach (var vformat in videoFormats)
        {
            var isMerged = vformat.AudioCodec != null && vformat.AudioCodec != "none";
            var vsize = vformat.FileSize ?? vformat.ApproximateFileSize ?? 0;
        
            if (vsize > options.MaxFilesize) break;

            if (isMerged || audioFormats.IsEmpty())
            {
                // premerged format or no audio, don't need to pick audio separately
                var score = GetVideoFormatScore(vformat);
                if (!score.HasValue || score < bestScore) continue;
                
                bestScore = score.Value;
                choice = (vformat, null);
            }
            else
            {
                foreach (var aformat in audioFormats)
                {
                    var asize = aformat.FileSize ?? aformat.ApproximateFileSize ?? 0;
                    var totalSize = vsize + asize;

                    if (totalSize > options.MaxFilesize) continue;
                    if (!IsAllowedCombination(vformat, aformat)) continue;

                    var videoScore = GetVideoFormatScore(vformat);
                    var audioScore = GetAudioFormatScore(aformat, options.MaxFilesize);
                    
                    #if DEBUG
                    scores[(vformat.FormatId, aformat.FormatId)] = (videoScore ?? 0d, audioScore ?? 0d);
                    #endif

                    // one of them is ineligible
                    if (!videoScore.HasValue || !audioScore.HasValue)
                        continue;
                    
                    // size efficiency: peaks at target ratio (maxVideoSize / maxAudioSize), falls off below
                    // the idea is that pairs that get closest to the perfect ratio get the highest mult
                    var videoSizeBonus = Math.Clamp(vsize / (double)maxVideoSize, 0.01, 1.0);
                    var audioSizeBonus = Math.Clamp(asize / (double)maxAudioSize, 0.01, 1.0);
                
                    // this ensures a terrible audio track drags down the score of a huge video (and vice versa)
                    var totalScore = videoScore * videoSizeBonus * audioScore * audioSizeBonus;

                    if (totalScore < bestScore) continue;
                    
                    bestScore = totalScore;
                    choice = (vformat, aformat);
                }
            }
        }
		
        return choice;
    }
	
    private static FormatData? TryPickOptimalAudioFormat(IList<FormatData> audioFormats, IList<FormatData> videoFormats, DownloadOptions options)
    {
        double? bestScore = null;
        FormatData? pickedFormat = null;

        foreach (var format in audioFormats)
        {
            var score = GetAudioFormatScore(format, options.MaxFilesize);
            if (score == null || score <= bestScore) continue;

            bestScore = score.Value;
            pickedFormat = format;
        }

        // if we found an audio format without a video attached to it, we just roll with that
        if (pickedFormat != null) return pickedFormat;
		
        // otherwise; there are no audio-only formats. best we can do is return a video, which will presumably have both...
        foreach (var vformat in videoFormats)
        {
            var vsize = vformat.FileSize ?? vformat.ApproximateFileSize ?? 0;
            if (vsize > options.MaxFilesize) break;
            
            // if there's no (eligible) audio channels, that probably means this video is in a premerged format and we don't need to pick audio separately
            var score = GetVideoFormatScore(vformat);
            if (score == null || score <= bestScore) continue;
			
            // new optimal combination found
            bestScore = score.Value;
            pickedFormat = vformat;
        }

        return pickedFormat;
    }

    internal class PickedFormat
    {
        public required FormatData? VideoFormat { get; init; }
        public required FormatData? AudioFormat { get; init; }
        public required string FormatString { get; init; }
    }

    /// <summary>
    /// Scores a selected video format, so it can be prioritized against others
    /// (better resolutions should beat worse ones, better codecs should beat worse ones,
    /// videos closer to the upload limit are preferred)
    /// </summary>
    private static double? GetVideoFormatScore(FormatData format)
    {
        var asize = format.FileSize ?? format.ApproximateFileSize;
        
        var score = (1 + (asize ?? 0)) / 1e6d;

        if (!asize.HasValue)
            score /= 1e6; // we don't know this video's size; last resort

        // resolution score
        if (format is { Width: not null, Height: not null })
            score *= 1.0 + (format.Width.Value * format.Height.Value / 1e6);
    
        // rate by codec multiplier: prioritize shiny modern stuff like AV1 or H265 over older turd codecs like H264
        var matchedScoreMult = format.VideoCodec != null
            ? VideoFormatQualityRatio.FirstOrDefault(data => data.FormatPattern.IsMatch(format.VideoCodec))
            : default;
        
        if (matchedScoreMult != default)
            score *= matchedScoreMult.ScoreMult;

        // ew watermark
        if (format.FormatNote?.Contains("watermark") == true)
            score /= 1e6;

        return score;
    }
	
    /// <summary>
    /// Scores a selected audio format, so it can be prioritized against others
    /// (i.e. better bitrates should trump worse ones, better codecs should beat worse ones,
    /// audio closer to the upload limit is preferred)
    /// </summary>
    private static double? GetAudioFormatScore(FormatData format, long sizeBudget)
    {
        var asize = format.FileSize ?? format.ApproximateFileSize ?? 0;

        if (sizeBudget < asize)
            return null;
		
        var resScore = format.AudioBitrate ?? format.Bitrate ?? 1;
        var matchedScoreMult = format.AudioCodec != null
            ? AudioFormatQualityRatio.FirstOrDefault(data => data.FormatPattern.IsMatch(format.AudioCodec))
            : default;

        var score = (1d + asize) * resScore;

        if (matchedScoreMult != default)
            score *= matchedScoreMult.ScoreMult;
		
        if (format.AudioCodec == null)
            score /= 1e6; // last resort
		
        return score;
    }

    /// <summary>
    /// Some combinations can't be combined due to container restrictions (ie: webm video and m4a will result in an unembeddable MKV)
    /// </summary>
    private static bool IsAllowedCombination(FormatData vformat, FormatData aformat)
    {
        // m4a audio can't be embedded in webm containers
        if (vformat.Extension != "mp4" && aformat.Extension == "m4a")
            return false;

        return true;
    }
	
    private static readonly Regex FormatRegex = new("^(hevc.*|h265.*|vp0?9.*|avc.*|h264.*)");

    private static IList<FormatData> GetEligibleVideos(IList<FormatData> formats, bool allowUnknownVcodec)
    {
        return formats
            .Where(f => (allowUnknownVcodec && f.VideoCodec is "unknown" or null)
                        || (f.VideoCodec != null && FormatRegex.IsMatch(f.VideoCodec))
                        || f.VideoExtension is not ("none" or null))
            .ToList();
    }

    private static IList<FormatData> GetEligibleAudio(IList<FormatData> formats)
    {
        return formats
            // being "null" isn't the same as being "none"; "none" means it explicitly doesn't have that media inside
            .Where(f => f.VideoCodec == "none" && f.AudioCodec != "none")
            .Where(f => f.AudioCodec != "ec-3" && f.AudioCodec != "ac-3") // what the FUCK is that ABSOLUTELY PROPRIETARY https://i.imgur.com/op8gqKO.jpeg
            .ToList();
    }
}