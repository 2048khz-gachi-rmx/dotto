namespace Dotto.Ai.Abstractions;

public interface IPromptRenderer
{
    Task<string> RenderAsync(string template, object context, CancellationToken cancellationToken = default);
}
