using Dotto.Infrastructure.Downloader.Contracts.Models;
using Dotto.Infrastructure.Downloader.Contracts.Models.Metadata;
using Dotto.Infrastructure.Downloader.YtdlDownloader;
using Shouldly;

namespace Dotto.Tests.UnitTests.Services;

public class YtdlFormatPickerTests : TestFixtureBase
{
    private readonly YtdlFormatPicker _picker = new();

    [Test]
    public void PickFormat_ShouldReturnFallbackWhenNoFormats()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats = [],
            Resolution = "1080p",
            VideoCodec = "h264",
            FormatId = "test_id"
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat.ShouldNotBeNull();
        result.AudioFormat.ShouldBeNull();
        result.FormatString.ShouldBe("test_id");
    }
        
    [Test]
    public void PickFormat_ShouldDeprioritizeUnknown()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new() { VideoCodec = "unknown", FormatId = "unknown_format" },
                new() { VideoCodec = "h264", FormatId = "h264_format" }
            ]
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat?.FormatId.ShouldBe("h264_format");
    }      
    
    [Test]
    public void PickFormat_ShouldDeprioritizeWatermark()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new() { FileSize = 100, VideoCodec = "h264", FormatId = "watermarked_format", FormatNote = "watermarked" },
                new() { FileSize = 80, VideoCodec = "h264", FormatId = "good_format" }
            ]
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat?.FormatId.ShouldBe("good_format");
    }
    
    [Test]
    public void PickFormat_ShouldPickWatermarkIfNoOtherChoice()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new() { FileSize = 10, VideoCodec = "h264", FormatId = "watermarked_format", FormatNote = "watermarked" },
                new() { FileSize = 9999, VideoCodec = "h264", FormatId = "unpickable_format" }
            ]
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat?.FormatId.ShouldBe("watermarked_format");
    }
    
    [Test] // in instagram, i think unknown is the video with better quality...? so we don't deprioritize it there
    public void PickFormat_ShouldNotDeprioritizeUnknownForInstagram()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Extractor = "Instagram",
            Formats =
            [
                new() { VideoCodec = "unknown", FormatId = "unknown_format" },
                new() { VideoCodec = "h264", FormatId = "h264_format" }
            ]
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat?.FormatId.ShouldBe("unknown_format");
    }
    
    [Test]
    public void PickFormat_ShouldHandleAllUnknown()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new()
                {
                    VideoCodec = null,
                    AudioCodec = null,
                    FormatId = "0",
                    Extension = "mp4"
                },
            ]
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat?.FormatId.ShouldBe("0");
    }

    [Test]
    public void TryPickOptimalFormat_ShouldReturnBestMergedFormat()
    {
        // Arrange
        var videoFormats = new List<FormatData>
        {
            new() { FormatId = "video1", VideoCodec = "h264", AudioCodec = "none", FileSize = 500 },
            new() { FormatId = "video2", VideoCodec = "hevc", AudioCodec = "aac", FileSize = 450 }
        };
        var audioFormats = new List<FormatData>();
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.TryPickOptimalFormat(audioFormats, videoFormats, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat?.FormatId.ShouldBe("video2");
        result.AudioFormat.ShouldBeNull();
    }

    [Test]
    public void TryPickOptimalFormat_ShouldPreferBetterCodecAndBalancedSplit()
    {
        // Arrange
        var videoFormats = new List<FormatData>
        {
            new() { FormatId = "video1", VideoCodec = "h264", AudioCodec = "none", FileSize = 500 },
            new() { FormatId = "video2", VideoCodec = "hevc", AudioCodec = "none", FileSize = 450 }
        };
        var audioFormats = new List<FormatData>
        {
            new() { FormatId = "audio1", AudioCodec = "aac", FileSize = 200 },
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.TryPickOptimalFormat(audioFormats, videoFormats, options);

        // Assert
        result.ShouldNotBeNull();
        // hevc (450) beats h264 (500) on codec multiplier despite being smaller
        result.VideoFormat?.FormatId.ShouldBe("video2");
        result.AudioFormat?.FormatId.ShouldBe("audio1");
    }

    [Test]
    public void TryPickOptimalFormat_ShouldReturnNullWhenNoValidCombination()
    {
        // Arrange
        var videoFormats = new List<FormatData>
        {
            new() { FormatId = "video1", VideoCodec = "h264", AudioCodec = "none", FileSize = 1500 }
        };
        var audioFormats = new List<FormatData>();
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.TryPickOptimalFormat(audioFormats, videoFormats, options);

        // Assert
        result.ShouldBeNull();
    }

    [Test]
    public void PickFormat_ShouldHandleEmptyFormatsList()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats = [],
            Resolution = "720p",
            VideoCodec = "vp9",
            FormatId = "empty_format"
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat.ShouldNotBeNull();
        result.AudioFormat.ShouldBeNull();
        result.FormatString.ShouldBe("empty_format");
    }

    [Test]
    public void PickFormat_ShouldPreferSupportedCodecs()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new() { FormatId = "vp9", VideoCodec = "vp9", AudioCodec = "none" },
                new() { FormatId = "h264", VideoCodec = "h264", AudioCodec = "none" }
            ]
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat?.FormatId.ShouldBe("vp9");
    }

    [Test]
    public void TryPickOptimalFormat_ShouldHandleEmptyLists()
    {
        // Arrange
        var videoFormats = new List<FormatData>();
        var audioFormats = new List<FormatData>();
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.TryPickOptimalFormat(audioFormats, videoFormats, options);

        // Assert
        result.ShouldBeNull();
    }

    [Test]
    public void TryPickOptimalFormat_ShouldHandleIncompatibleCombination()
    {
        // Arrange
        var videoFormats = new List<FormatData>
        {
            new()
            {
                FormatId = "video1", VideoCodec = "hevc", AudioCodec = "none", Extension = "mp4", Width = 100,
                Height = 100
            },
            new()
            {
                FormatId = "video2", VideoCodec = "vp9", AudioCodec = "none", Extension = "webm", Width = 200,
                Height = 200
            }
        };
        var audioFormats = new List<FormatData>
        {
            new() { FormatId = "audio1", AudioCodec = "aac", Extension = "m4a" }
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.TryPickOptimalFormat(audioFormats, videoFormats, options);

        // Assert
        result.ShouldNotBeNull();
        // Even though the vp9 video is better, the only audio option we have is an m4a which is incompatible with webms
        // So we expect to receive the mp4 with audio
        result.VideoFormat?.FormatId.ShouldBe("video1");
        result.AudioFormat?.FormatId.ShouldBe("audio1");
    }

    [Test]
    public void TryPickOptimalFormat_ShouldPrioritizeCloserToPerfectRatio()
    {
        // Arrange
        /* The allowed (sub-limit) candidates are:
         * 900 + 30 => 930
         * 900 + 70 => 970
         * 950 + 30 => 980
         *
         * The reason we actually want 970 is that the distribution is closer to our perfect ratio defined in the picker.
         * 980 may sound better because it gets closer to the cap, but in reality it means picking dogshit audio, which is suboptimal
         */
        var videoFormats = new List<FormatData>
        {
            new() { FormatId = "PICKME", VideoCodec = "h264", FileSize = 900 },
            new() { FormatId = "theres_a_better_one", VideoCodec = "h264", FileSize = 950 },
            new() { FormatId = "too_large", VideoCodec = "h264", FileSize = 990 },
        };
        var audioFormats = new List<FormatData>
        {
            new() { FormatId = "bad_audio", AudioCodec = "aac", FileSize = 30 },
            new() { FormatId = "good_audio", AudioCodec = "mp3", FileSize = 70 }
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.TryPickOptimalFormat(audioFormats, videoFormats, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat?.FormatId.ShouldBe("PICKME");
        result.AudioFormat?.FormatId.ShouldBe("good_audio");
    }

    [Test]
    public void PickFormat_ShouldPickAudioOnlyFormat()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new() { FormatId = "audio1", VideoCodec = "none", AudioCodec = "aac" },
                new() { FormatId = "audio2", VideoCodec = "none", AudioCodec = "mp3" }
            ]
        };
        var options = new DownloadOptions { MaxFilesize = 1000, AudioOnly = true };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat.ShouldBeNull();
        result.AudioFormat.ShouldNotBeNull();
    }

    [Test]
    public void PickFormat_ShouldPickBestAudioFormat()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new() { FormatId = "audio1", VideoCodec = "none", AudioCodec = "aac", Bitrate = 150, FileSize = 300 },
                new() { FormatId = "audio2", VideoCodec = "none", AudioCodec = "aac", Bitrate = 200, FileSize = 290 }
            ]
        };
        var options = new DownloadOptions { MaxFilesize = 1000, AudioOnly = true };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat.ShouldBeNull();
        result.AudioFormat?.FormatId.ShouldBe("audio2"); // aac should be preferred over mp3
    }
    
    [Test]
    public void PickFormat_ShouldDownloadAudioWhenNoVideo()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new() { FormatId = "audio1", VideoCodec = "none", AudioCodec = "mp3" },
                new() { FormatId = "audio2", VideoCodec = "none", AudioCodec = "opus" }
            ]
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat.ShouldBeNull();
        result.AudioFormat?.FormatId.ShouldBe("audio2"); // opus should be preferred over mp3
    }

    [Test]
    public void TryPickOptimalFormat_ShouldReturnBestAudioOnlyFormat()
    {
        // Arrange
        var audioFormats = new List<FormatData>
        {
            new() { FormatId = "audio1", AudioCodec = "mp3", FileSize = 200 },
            new() { FormatId = "audio2", AudioCodec = "opus", FileSize = 190 }
        };
        var videoFormats = new List<FormatData>();
        var options = new DownloadOptions { MaxFilesize = 1000, AudioOnly = true };

        // Act
        var result = _picker.TryPickOptimalFormat(audioFormats, videoFormats, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat.ShouldBeNull();
        result.AudioFormat?.FormatId.ShouldBe("audio2"); // opus should be preferred over mp3, even at lower filesize/bitrate
    }

    [Test]
    public void TryPickOptimalFormat_ShouldReturnNullWhenNoValidAudio()
    {
        // Arrange
        var videoFormats = new List<FormatData>
        {
            new() { FormatId = "video1", VideoCodec = "h264", AudioCodec = "none", FileSize = 500 }
        };
        var audioFormats = new List<FormatData>
        {
            new() { FormatId = "audio1", AudioCodec = "mp3", FileSize = 600 } // too large
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.TryPickOptimalFormat(audioFormats, videoFormats, options);

        // Assert
        result.ShouldBeNull();
    }

    [Test]
    public void PickFormat_ShouldHandleAudioOnlyWithNoValidOptions()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new() { FormatId = "audio1", VideoCodec = "none", AudioCodec = "mp3", FileSize = 2000 }
            ]
        };
        var options = new DownloadOptions { MaxFilesize = 1000, AudioOnly = true };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldBeNull();
    }
    
    [Test] // https://www.youtube.com/watch?v=rurhk1hadp8
    // You're a big format
    public void PickFormat_ForYou()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new() { FormatId = "die", VideoCodec = "none", AudioCodec = "ec-3", FileSize = 1 },
                new() { FormatId = "also die", VideoCodec = "none", AudioCodec = "ec-3", FileSize = 3 },
                new() { FormatId = "die", VideoCodec = "none", AudioCodec = "ac-3", FileSize = 1 },
                new() { FormatId = "also die", VideoCodec = "none", AudioCodec = "ac-3", FileSize = 3 },
                new() { FormatId = "good :)))", VideoCodec = "none", AudioCodec = "not-ec-3", FileSize = 2 }
            ]
        };
        
        var options = new DownloadOptions { MaxFilesize = 5 };
        
        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat.ShouldBeNull();
        result.AudioFormat.ShouldNotBeNull().FormatId.ShouldBe("good :)))");
    }

    [Test]
    public void PickFormat_XIsAnnoying()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new() { FormatId = "hls-audio-32000-Audio", VideoCodec = "none", AudioCodec = null, Extension = "mp4", FileSize = null },
                new() { FormatId = "hls-audio-64000-Audio", VideoCodec = "none", AudioCodec = null, Extension = "mp4", FileSize = null },
                new() { FormatId = "hls-audio-128000-Audio", VideoCodec = "none", AudioCodec = null, Extension = "mp4", FileSize = null },
                new() { FormatId = "http-256", Width = 480, Height = 270, VideoExtension = "mp4", AudioExtension = "none", Extension = "mp4", FileSize = 4277312 },
                new() { FormatId = "hls-150", Width = 480, Height = 270, VideoCodec = "avc1.4D4015", AudioCodec = "none", Extension = "mp4", FileSize = null },
                new() { FormatId = "http-832", Width = 640, Height = 480, VideoExtension = "mp4", AudioExtension = "none", Extension = "mp4", FileSize = 13901264 },
                new() { FormatId = "hls-417", Width = 640, Height = 480, VideoCodec = "avc1.4D401E", AudioCodec = "none", Extension = "mp4", FileSize = null },
                new() { FormatId = "http-2176", Width = 1280, Height = 720, VideoExtension = "mp4", AudioExtension = "none", Extension = "mp4", FileSize = 36357152 },
                new() { FormatId = "hls-1164", Width = 1280, Height = 720, VideoCodec = "avc1.64001F", AudioCodec = "none", Extension = "mp4", FileSize = null },
                new() { FormatId = "http-10368", Width = 1920, Height = 1080, VideoExtension = "mp4", AudioExtension = "none", Extension = "mp4", FileSize = 173231136 },
                new() { FormatId = "hls-2314", Width = 1920, Height = 1080, VideoCodec = "avc1.640032", AudioCodec = "none", Extension = "mp4", FileSize = null }
            ]
        };
        
        var options = new DownloadOptions { MaxFilesize = 104857600 };
        
        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat.ShouldNotBeNull().FormatId.ShouldBe("http-2176"); // biggest filesize below limit
        result.AudioFormat.ShouldNotBeNull().FormatId.ShouldBe("hls-audio-128000-Audio"); // best available audio, still below limit
    }
    
    // Regression for https://www.youtube.com/watch?v=yEBQHXW5POY: a source-quality 1080p vp9 stream was picked
    // over an av1 stream ~24% smaller at the same resolution.
    // Issue was: raw size is already a score factor inside, but it was getting weighed against the budget a second time
    // (with perfect ratio score bonus), so a better codec got punished by roughly size^2 even with the codec mult
    [Test]
    public void PickFormat_ShouldPreferEfficientCodecOverLargerBitrate()
    {
        // Arrange
        var metadata = new DownloadedMediaMetadata
        {
            Formats =
            [
                new()
                {
                    FormatId = "248", VideoCodec = "vp9", AudioCodec = "none", Extension = "webm",
                    VideoExtension = "webm", AudioExtension = "none", Width = 1920, Height = 1080, FileSize = 7_577_041
                },
                new()
                {
                    FormatId = "399", VideoCodec = "av01.0.08M.08", AudioCodec = "none", Extension = "webm",
                    VideoExtension = "webm", AudioExtension = "none", Width = 1920, Height = 1080, FileSize = 5_788_899
                },
                new()
                {
                    FormatId = "251", VideoCodec = "none", AudioCodec = "opus", Extension = "webm",
                    VideoExtension = "none", AudioExtension = "webm", FileSize = 588_360, AudioBitrate = 125.849
                }
            ]
        };
        var options = new DownloadOptions { MaxFilesize = 10 * 1024 * 1024 };

        // Act
        var result = _picker.PickFormat(metadata, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat?.FormatId.ShouldBe("399");
        result.AudioFormat?.FormatId.ShouldBe("251");
    }

    // The pair should stay near the ideal video/audio split (80/20). Real candidates never total the same size,
    // so the split has to beat raw total size: 75mb+20mb (79/21, near ideal) must beat 50mb+49mb (51/49), even
    // though the latter is a bigger pair overall and has a bigger audio track. The balance term used to only
    // penalize a track that was too *small* and never one that was too *large*, so a bloated audio track won.
    [Test]
    public void TryPickOptimalFormat_ShouldPreferBalancedSplitOverBiggerTotal()
    {
        // Arrange
        var videoFormats = new List<FormatData>
        {
            new() { FormatId = "video_75mb", VideoCodec = "h264", FileSize = 75_000_000 },
            new() { FormatId = "video_50mb", VideoCodec = "h264", FileSize = 50_000_000 }
        };
        var audioFormats = new List<FormatData>
        {
            new() { FormatId = "audio_20mb", AudioCodec = "aac", FileSize = 20_000_000, AudioBitrate = 128 },
            new() { FormatId = "audio_49mb", AudioCodec = "aac", FileSize = 49_000_000, AudioBitrate = 128 }
        };
        var options = new DownloadOptions { MaxFilesize = 100_000_000 };

        // Act
        var result = _picker.TryPickOptimalFormat(audioFormats, videoFormats, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat?.FormatId.ShouldBe("video_75mb");
        result.AudioFormat?.FormatId.ShouldBe("audio_20mb");
    }

    // Testing the audio/video balance: a video that eats almost the whole budget leaves fuckall room for audio,
    // and that pair should lose to a smaller video that can afford decent audio.
    [Test]
    public void TryPickOptimalFormat_ShouldAvoidBudgetHoggingVideoWithScrapAudio()
    {
        // Arrange
        var videoFormats = new List<FormatData>
        {
            new() { FormatId = "video_hog", VideoCodec = "h264", FileSize = 980 },
            new() { FormatId = "video_accomodating", VideoCodec = "h264", FileSize = 800 }
        };
        var audioFormats = new List<FormatData>
        {
            new() { FormatId = "audio_shit", AudioCodec = "aac", FileSize = 10, AudioBitrate = 128 },
            new() { FormatId = "audio_good", AudioCodec = "aac", FileSize = 150, AudioBitrate = 128 }
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.TryPickOptimalFormat(audioFormats, videoFormats, options);

        // Assert
        result.ShouldNotBeNull();
        result.VideoFormat?.FormatId.ShouldBe("video_accomodating");
        result.AudioFormat?.FormatId.ShouldBe("audio_good");
    }
}