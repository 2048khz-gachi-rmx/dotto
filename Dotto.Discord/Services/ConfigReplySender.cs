using Microsoft.Extensions.Logging;
using NetCord.Rest;

namespace Dotto.Discord.Services;

public interface IConfigReplySender
{
    /// <summary>Sends the response to the invoking user's DMs. Returns false when it fails.</summary>
    Task<bool> TrySendDirectMessageAsync(
        ulong userId, string content, CancellationToken ct = default);
}

internal sealed class ConfigReplySender(
    RestClient client,
    ILogger<ConfigReplySender> logger) : IConfigReplySender
{
    public async Task<bool> TrySendDirectMessageAsync(
        ulong userId, string content, CancellationToken ct = default)
    {
        try
        {
            var channel = await client.GetDMChannelAsync(userId, cancellationToken: ct);
            await client.SendMessageAsync(
                channel.Id,
                new MessageProperties { Content = content },
                cancellationToken: ct);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Could not send a configuration response to user {UserId} via DM",
                userId);

            return false;
        }
    }
}

public static class ConfigCommandDelivery
{
    public const string DirectMessageFallback =
        "I couldn't send you a DM. Enable direct messages from this server, " +
        "or use `/config` for an ephemeral response.";

    /// <summary>
    /// Sends through the DM path; when it fails, replies with a short instruction instead
    /// so values are never leaked into a public channel.
    /// </summary>
    public static async Task DeliverAsync(
        Func<Task<bool>> sendDirectMessage,
        Func<string, Task> reply,
        string content)
    {
        if (await sendDirectMessage())
            return;

        await reply(DirectMessageFallback);
    }
}
