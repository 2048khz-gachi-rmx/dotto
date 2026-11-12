using Dotto.Discord.CommandHandlers.Config;
using Dotto.Discord.Models.Config;
using Dotto.Discord.Services;
using Dotto.Domain.Entities;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace Dotto.Discord.Commands.Config;

public class TextCommand(
    ConfigCommandHandler handler,
    IConfigReplySender replySender) : CommandModule<CommandContext>
{
    [Command("config")]
    public Task Config(
        string operation,
        string? key = null,
        string? value = null,
        ConfigScopeOption scope = ConfigScopeOption.Global,
        string? user = null)
    {
        return operation.ToLowerInvariant() switch
        {
            "set" when key != null && value != null => Send(
                handler.SetAsync<ReplyMessageProperties>(
                    Context.User.Id,
                    Context.Guild?.Id,
                    Context.Message.ChannelId,
                    (ConfigurationScope)(int)scope,
                    ParseUserId(user),
                    key,
                    value)),
            "unset" when key != null => Send(
                handler.UnsetAsync<ReplyMessageProperties>(
                    Context.User.Id,
                    Context.Guild?.Id,
                    Context.Message.ChannelId,
                    (ConfigurationScope)(int)scope,
                    ParseUserId(user),
                    key)),
            "get" when key != null => Send(
                handler.GetAsync<ReplyMessageProperties>(
                    Context.User.Id,
                    Context.Guild?.Id,
                    Context.Message.ChannelId,
                    (ConfigurationScope)(int)scope,
                    ParseUserId(user),
                    key)),
            "list" => Send(
                handler.ListAsync<ReplyMessageProperties>(
                    Context.User.Id,
                    Context.Guild?.Id,
                    Context.Message.ChannelId,
                    (ConfigurationScope)(int)scope,
                    ParseUserId(user))),
            _ => ReplyAsync(new ReplyMessageProperties
            {
                Content = "Usage: `.config <set|unset|get|list> <key> [value] [global|guild|channel|user] [userId]`"
            })
        };
    }

    private Task Send(Task<ReplyMessageProperties> resultTask)
        => SendResult(resultTask);

    private static ulong? ParseUserId(string? value)
        => ulong.TryParse(value, out var id) && id != 0 ? id : null;

    private async Task SendResult(Task<ReplyMessageProperties> resultTask)
    {
        var result = await resultTask;

        await ConfigCommandDelivery.DeliverAsync(
            () => replySender.TrySendDirectMessageAsync(Context.User.Id, result.Content ?? string.Empty),
            content => ReplyAsync(new ReplyMessageProperties { Content = content }),
            result.Content ?? string.Empty);
    }
}
