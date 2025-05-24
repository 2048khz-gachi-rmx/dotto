using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Hosting.Services.Commands;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.Commands;

namespace Dotto.Discord.ResultHandlers;

/// <summary>
/// Custom text command handler.
/// Wraps errors in a cute embed and shuts up the "Command not found" error
/// </summary>
public class DottoCommandResultHandler<TContext> : ICommandResultHandler<TContext>
    where TContext : ICommandContext
{
    public ValueTask HandleResultAsync(IExecutionResult result, TContext context, GatewayClient client, ILogger logger, IServiceProvider services)
    {
        if (result is not IFailResult failResult
            || result is NotFoundResult) /* why do we error by default if a text command isn't found this is fucking retarded */
            return default;

        var resultMessage = failResult.Message;
        var message = context.Message;

        if (failResult is IExceptionResult exceptionResult)
            logger.LogError(exceptionResult.Exception, "Execution of a command with content '{Content}' failed with an exception", message.Content);
        else
            logger.LogDebug("Execution of a command with content '{Content}' failed with '{Message}'", message.Content, resultMessage);

        var response = Common.GetErrorEmbed<ReplyMessageProperties>(resultMessage);

        return new(message.ReplyAsync(response));
    }
}