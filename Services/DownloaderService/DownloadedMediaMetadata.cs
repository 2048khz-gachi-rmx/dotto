using System.Text.Json.Serialization;
using YoutubeDLSharp.Metadata;

namespace Services.DownloaderService;

// The metadata class provided by YoutubeDlSharp uses Newtonsoft's JSON attributes, but i want to use System.Text.Json
public class DownloadedMediaMetadata
{
    [JsonPropertyName("extractor")]
    public string? Extractor { get; set; }

    [JsonPropertyName("extractor_key")]
    public string? ExtractorKey { get; set; }

    [JsonPropertyName("entries")]
    public VideoData[]? Entries { get; set; }

    [JsonPropertyName("id")]
    public string? ID { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("formats")]
    public FormatData[]? Formats { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("ext")]
    public string? Extension { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("format_id")]
    public string? FormatID { get; set; }

    [JsonPropertyName("player_url")]
    public string? PlayerUrl { get; set; }

    [JsonPropertyName("direct")]
    public bool Direct { get; set; }

    [JsonPropertyName("alt_title")]
    public string? AltTitle { get; set; }

    [JsonPropertyName("display_id")]
    public string? DisplayID { get; set; }

    [JsonPropertyName("thumbnails")]
    public ThumbnailData[]? Thumbnails { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("uploader")]
    public string? Uploader { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    // Not System.Text.Json's converters... but we don't need these fields
    /*
    [JsonConverter(typeof (UnixTimestampConverter))]
    [JsonPropertyName("release_timestamp")]
    public DateTime? ReleaseTimestamp { get; set; }

    [JsonConverter(typeof (CustomDateTimeConverter))]
    [JsonPropertyName("release_date")]
    public DateTime? ReleaseDate { get; set; }
    
    [JsonConverter(typeof (UnixTimestampConverter))]
    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    [JsonConverter(typeof (CustomDateTimeConverter))]
    [JsonPropertyName("upload_date")]
    public DateTime? UploadDate { get; set; }

    [JsonConverter(typeof (UnixTimestampConverter))]
    [JsonPropertyName("modified_timestemp")]
    public DateTime? ModifiedTimestamp { get; set; }

    [JsonConverter(typeof (CustomDateTimeConverter))]
    [JsonPropertyName("modified_date")]
    public DateTime? ModifiedDate { get; set; }
    */
    
    [JsonPropertyName("uploader_id")]
    public string? UploaderID { get; set; }

    [JsonPropertyName("uploader_url")]
    public string? UploaderUrl { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("channel_id")]
    public string? ChannelID { get; set; }

    [JsonPropertyName("channel_url")]
    public string? ChannelUrl { get; set; }

    [JsonPropertyName("channel_follower_count")]
    public long? ChannelFollowerCount { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("subtitles")]
    public Dictionary<string, SubtitleData[]>? Subtitles { get; set; }

    [JsonPropertyName("automatic_captions")]
    public Dictionary<string, SubtitleData[]>? AutomaticCaptions { get; set; }

    [JsonPropertyName("duration")]
    public float? Duration { get; set; }

    [JsonPropertyName("view_count")]
    public long? ViewCount { get; set; }

    [JsonPropertyName("concurrent_view_count")]
    public long? ConcurrentViewCount { get; set; }

    [JsonPropertyName("like_count")]
    public long? LikeCount { get; set; }

    [JsonPropertyName("dislike_count")]
    public long? DislikeCount { get; set; }

    [JsonPropertyName("repost_count")]
    public long? RepostCount { get; set; }

    [JsonPropertyName("average_rating")]
    public double? AverageRating { get; set; }

    [JsonPropertyName("comment_count")]
    public long? CommentCount { get; set; }

    [JsonPropertyName("comments")]
    public CommentData[]? Comments { get; set; }

    [JsonPropertyName("age_limit")]
    public int? AgeLimit { get; set; }

    [JsonPropertyName("webpage_url")]
    public string? WebpageUrl { get; set; }

    [JsonPropertyName("categories")]
    public string[]? Categories { get; set; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("cast")]
    public string[]? Cast { get; set; }

    [JsonPropertyName("is_live")]
    public bool? IsLive { get; set; }

    [JsonPropertyName("was_live")]
    public bool? WasLive { get; set; }

    /*
    [JsonConverter(typeof (JsonStringEnumConverter<LiveStatus>))]
    [JsonPropertyName("live_status")]
    public LiveStatus LiveStatus { get; set; }
    
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [JsonPropertyName("availability")]
    public Availability? Availability { get; set; }
    */
    
    [JsonPropertyName("start_time")]
    public float? StartTime { get; set; }

    [JsonPropertyName("end_time")]
    public float? EndTime { get; set; }

    [JsonPropertyName("playable_in_embed")]
    public bool? PlayableInEmbed { get; set; }

    [JsonPropertyName("chapters")]
    public ChapterData[]? Chapters { get; set; }

    [JsonPropertyName("chapter")]
    public string? Chapter { get; set; }

    [JsonPropertyName("chapter_number")]
    public int? ChapterNumber { get; set; }

    [JsonPropertyName("chapter_id")]
    public string? ChapterId { get; set; }

    [JsonPropertyName("series")]
    public string? Series { get; set; }

    [JsonPropertyName("series_id")]
    public string? SeriesId { get; set; }

    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("season_number")]
    public int? SeasonNumber { get; set; }

    [JsonPropertyName("season_id")]
    public string? SeasonId { get; set; }

    [JsonPropertyName("episode")]
    public string? Episode { get; set; }

    [JsonPropertyName("episode_number")]
    public int? EpisodeNumber { get; set; }

    [JsonPropertyName("episode_id")]
    public string? EpisodeId { get; set; }

    [JsonPropertyName("track")]
    public string? Track { get; set; }

    [JsonPropertyName("track_number")]
    public int? TrackNumber { get; set; }

    [JsonPropertyName("track_id")]
    public string? TrackId { get; set; }

    [JsonPropertyName("artist")]
    public string? Artist { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("album")]
    public string? Album { get; set; }

    [JsonPropertyName("album_type")]
    public string? AlbumType { get; set; }

    [JsonPropertyName("album_artist")]
    public string? AlbumArtist { get; set; }

    [JsonPropertyName("disc_number")]
    public int? DiscNumber { get; set; }

    [JsonPropertyName("release_year")]
    public int? ReleaseYear { get; set; }

    [JsonPropertyName("composer")]
    public string? Composer { get; set; }

    [JsonPropertyName("section_start")]
    public long? SectionStart { get; set; }

    [JsonPropertyName("section_end")]
    public long? SectionEnd { get; set; }

    [JsonPropertyName("rows")]
    public long? StoryboardFragmentRows { get; set; }

    [JsonPropertyName("columns")]
    public long? StoryboardFragmentColumns { get; set; }
    
    // These are added by me:
    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }
    
    [JsonPropertyName("vcodec")]
    public string? VideoCodec { get; set; }
}