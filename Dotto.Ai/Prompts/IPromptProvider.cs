namespace Dotto.Ai.Prompts;

public interface IPromptProvider
{
    /// <summary>
    /// Renders the prompt registered under <paramref name="key"/> using the
    /// provided template context. Returns <c>null</c> if no prompt is
    /// registered for the key.
    /// </summary>
    Task<string?> RenderAsync(string key, object context, CancellationToken cancellationToken = default);
}
