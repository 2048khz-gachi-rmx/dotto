using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Discord.ResultHandlers;

public class DottoApplicationCommandServiceResultHandler<TContext> : IApplicationCommandResultHandler<TContext>
    where TContext : IApplicationCommandContext
{
    public ValueTask HandleResultAsync(IExecutionResult result, TContext context, GatewayClient? client, ILogger logger, IServiceProvider services)
    {
        if (result is not IFailResult failResult)
            return default;

        var resultMessage = failResult.Message;

        var interaction = context.Interaction;

        if (failResult is IExceptionResult exceptionResult)
            logger.LogError(exceptionResult.Exception, "Execution of an interaction '{Name}' failed with an exception", interaction.Data.Name);
        else
            logger.LogDebug("Execution of an interaction of custom ID '{Id}' failed with '{Message}'", interaction.Id, resultMessage);

        var response = Common.GetErrorEmbed<InteractionMessageProperties>(resultMessage);

        var task = interaction.SendResponseAsync(InteractionCallback.Message(response))
            .ContinueWith(t =>
            {
                if (t.Exception!.InnerException is RestException { Error.Code: 40060 }) // "Interaction has already been acknowledged."
                {
                    // Netcord doesn't play very well when an exception is thrown after the interaction is deferred.
                    // I didn't find a way to check if the interaction was deferred, so instead of LBYL, lets blindly try responding,
                    // and if it fails, try to modify the followup instead
                    return interaction.ModifyResponseAsync(opt =>
                        opt.WithContent(response.Content)
                            .WithEmbeds(response.Embeds));
                }

                return Task.CompletedTask;
            }, TaskContinuationOptions.OnlyOnFaulted);
        
        return new(task);
    }
}