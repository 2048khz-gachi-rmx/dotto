using NetCord.Rest;

namespace Dotto.Ai.Models;

public record AgentAttachment(Stream Stream, string FileName)
{
    /// <summary>
    /// Set by the handler after uploading to Discord directly.
    /// </summary>
    public AttachmentProperties? DiscordAttachment { get; set; }

    /// <summary>
    /// Set by the handler after uploading to S3 when the file is too large.
    /// </summary>
    public Uri? S3AttachmentUrl { get; set; }
}
