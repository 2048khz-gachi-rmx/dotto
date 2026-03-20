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
    public void TryPickOptimalFormat_ShouldReturnOptimizedCodec()
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
            new() { FormatId = "audio2", AudioCodec = "mp3", FileSize = 150 }
        };
        var options = new DownloadOptions { MaxFilesize = 1000 };

        // Act
        var result = _picker.TryPickOptimalFormat(audioFormats, videoFormats, options);

        // Assert
        result.ShouldNotBeNull();
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
}