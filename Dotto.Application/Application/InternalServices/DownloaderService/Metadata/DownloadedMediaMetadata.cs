using System.Text.Json.Serialization;

namespace Dotto.Application.InternalServices.DownloaderService.Metadata;

// The metadata class provided by YoutubeDlSharp uses Newtonsoft's JSON attributes, but i want to use System.Text.Json
public class DownloadedMediaMetadata
{
    [JsonPropertyName("extractor")]
    public string? Extractor { get; init; }

    [JsonPropertyName("extractor_key")]
    public string? ExtractorKey { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("formats")]
    public FormatData[]? Formats { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("ext")]
    public string? Extension { get; init; }

    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("format_id")]
    public string? FormatId { get; init; }

    [JsonPropertyName("player_url")]
    public string? PlayerUrl { get; init; }

    [JsonPropertyName("direct")]
    public bool Direct { get; init; }

    [JsonPropertyName("alt_title")]
    public string? AltTitle { get; init; }

    [JsonPropertyName("display_id")]
    public string? DisplayId { get; init; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("uploader")]
    public string? Uploader { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }

    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

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
    public string? UploaderId { get; init; }

    [JsonPropertyName("uploader_url")]
    public string? UploaderUrl { get; init; }

    [JsonPropertyName("channel")]
    public string? Channel { get; init; }

    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; init; }

    [JsonPropertyName("channel_url")]
    public string? ChannelUrl { get; init; }

    [JsonPropertyName("channel_follower_count")]
    public long? ChannelFollowerCount { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }

    [JsonPropertyName("duration")]
    public float? Duration { get; init; }

    [JsonPropertyName("view_count")]
    public long? ViewCount { get; init; }

    [JsonPropertyName("concurrent_view_count")]
    public long? ConcurrentViewCount { get; init; }

    [JsonPropertyName("like_count")]
    public long? LikeCount { get; init; }

    [JsonPropertyName("dislike_count")]
    public long? DislikeCount { get; init; }

    [JsonPropertyName("repost_count")]
    public long? RepostCount { get; init; }

    [JsonPropertyName("average_rating")]
    public double? AverageRating { get; init; }

    [JsonPropertyName("comment_count")]
    public long? CommentCount { get; init; }

    [JsonPropertyName("age_limit")]
    public int? AgeLimit { get; init; }

    [JsonPropertyName("webpage_url")]
    public string? WebpageUrl { get; init; }

    [JsonPropertyName("categories")]
    public string[]? Categories { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("cast")]
    public string[]? Cast { get; init; }

    [JsonPropertyName("is_live")]
    public bool? IsLive { get; init; }

    [JsonPropertyName("was_live")]
    public bool? WasLive { get; init; }

    /*
    [JsonConverter(typeof (JsonStringEnumConverter<LiveStatus>))]
    [JsonPropertyName("live_status")]
    public LiveStatus LiveStatus { get; set; }
    
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [JsonPropertyName("availability")]
    public Availability? Availability { get; set; }
    */
    
    [JsonPropertyName("start_time")]
    public float? StartTime { get; init; }

    [JsonPropertyName("end_time")]
    public float? EndTime { get; init; }

    [JsonPropertyName("playable_in_embed")]
    public bool? PlayableInEmbed { get; init; }

    [JsonPropertyName("chapter")]
    public string? Chapter { get; init; }

    [JsonPropertyName("chapter_number")]
    public int? ChapterNumber { get; init; }

    [JsonPropertyName("chapter_id")]
    public string? ChapterId { get; init; }

    [JsonPropertyName("series")]
    public string? Series { get; init; }

    [JsonPropertyName("series_id")]
    public string? SeriesId { get; init; }

    [JsonPropertyName("season")]
    public string? Season { get; init; }

    [JsonPropertyName("season_number")]
    public int? SeasonNumber { get; init; }

    [JsonPropertyName("season_id")]
    public string? SeasonId { get; init; }

    [JsonPropertyName("episode")]
    public string? Episode { get; init; }

    [JsonPropertyName("episode_number")]
    public int? EpisodeNumber { get; init; }

    [JsonPropertyName("episode_id")]
    public string? EpisodeId { get; init; }

    [JsonPropertyName("track")]
    public string? Track { get; init; }

    [JsonPropertyName("track_number")]
    public int? TrackNumber { get; init; }

    [JsonPropertyName("track_id")]
    public string? TrackId { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    [JsonPropertyName("genre")]
    public string? Genre { get; init; }

    [JsonPropertyName("album")]
    public string? Album { get; init; }

    [JsonPropertyName("album_type")]
    public string? AlbumType { get; init; }

    [JsonPropertyName("album_artist")]
    public string? AlbumArtist { get; init; }

    [JsonPropertyName("disc_number")]
    public int? DiscNumber { get; init; }

    [JsonPropertyName("release_year")]
    public int? ReleaseYear { get; init; }

    [JsonPropertyName("composer")]
    public string? Composer { get; init; }

    [JsonPropertyName("section_start")]
    public long? SectionStart { get; init; }

    [JsonPropertyName("section_end")]
    public long? SectionEnd { get; init; }

    [JsonPropertyName("rows")]
    public long? StoryboardFragmentRows { get; init; }

    [JsonPropertyName("columns")]
    public long? StoryboardFragmentColumns { get; init; }
    
    // These are added by me:
    [JsonPropertyName("resolution")]
    public string? Resolution { get; init; }
    
    [JsonPropertyName("vcodec")]
    public string? VideoCodec { get; init; }
    
    [JsonPropertyName("filename")]
    public string? FilePath { get; init; }
}