using NetCord.Rest;

namespace Dotto.Ai.Agents.Base;

public record AgentAttachment(Stream Stream, string FileName, long Length)
{
    /// <summary>
    /// Sandbox source path, recorded with the delivered media.
    /// </summary>
    public string? SourcePath { get; init; }

    public AttachmentProperties? DiscordAttachment { get; set; }

    public Uri? S3AttachmentUrl { get; set; }
}
