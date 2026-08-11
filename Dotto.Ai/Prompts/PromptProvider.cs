using Dotto.Ai.Abstractions;
using Dotto.Ai.Settings;
using Fluid;
using Microsoft.Extensions.Options;

namespace Dotto.Ai;

internal sealed class PromptProvider(IOptions<AiSettings> options) : IPromptProvider
{
    private readonly FluidParser _parser = new();
    private readonly Dictionary<string, PromptTemplate> _templates =
        options.Value.Prompts.ToDictionary(kvp => kvp.Key, kvp => new PromptTemplate(kvp.Value));

    private static readonly TemplateOptions TemplateOptions = new()
    {
        MemberAccessStrategy = new UnsafeMemberAccessStrategy()
    };

    public async Task<string?> RenderAsync(string key, object context, CancellationToken cancellationToken = default)
    {
        if (!_templates.TryGetValue(key, out var template))
            return null;

        var raw = await template.GetAsync(cancellationToken);

        if (!_parser.TryParse(raw, out var parsed, out var error))
            throw new InvalidOperationException($"Failed to parse prompt template '{key}': {error}");

        var fluidContext = new TemplateContext(context, TemplateOptions);
        return await parsed.RenderAsync(fluidContext);
    }
}
