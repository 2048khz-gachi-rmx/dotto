using System.Collections.Concurrent;
using Dotto.Ai.Settings;
using Dotto.Application.Configuration;
using Fluid;

namespace Dotto.Ai.Prompts;

/// <summary>
/// Caches template sources across interactions. The provider itself is scoped, because which
/// prompt a key points at is configurable per scope, but the file or embedded resource behind
/// a <see cref="PromptSource"/> is the same for everyone, including its cache/mtime handling.
/// </summary>
internal sealed class PromptTemplateCache
{
    private readonly ConcurrentDictionary<(PromptType Type, string Path), PromptTemplate> _templates = new();

    public PromptTemplate Get(PromptSource source)
        => _templates.GetOrAdd((source.Type, source.Path), _ => new PromptTemplate(source));
}

/// <summary>
/// Renders the prompt a key points at, using the interaction's effective
/// <see cref="AiSettings"/> (prompt definitions are overridable per scope).
/// </summary>
internal sealed class PromptProvider(
    IInteractionConfiguration configuration,
    PromptTemplateCache templates) : IPromptProvider
{
    private readonly FluidParser _parser = new();

    private static readonly TemplateOptions TemplateOptions = new()
    {
        MemberAccessStrategy = new UnsafeMemberAccessStrategy()
    };

    public async Task<string?> RenderAsync(string key, object context, CancellationToken cancellationToken = default)
    {
        var settings = await configuration.GetCurrentAsync<AiSettings>(cancellationToken);

        if (!settings.Prompts.TryGetValue(key, out var source))
            return null;

        var raw = await templates.Get(source).GetAsync(cancellationToken);

        if (!_parser.TryParse(raw, out var parsed, out var error))
            throw new InvalidOperationException($"Failed to parse prompt template '{key}': {error}");

        var fluidContext = new TemplateContext(context, TemplateOptions);
        return await parsed.RenderAsync(fluidContext);
    }
}
