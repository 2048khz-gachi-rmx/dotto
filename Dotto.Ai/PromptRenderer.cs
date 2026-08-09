using Dotto.Ai.Abstractions;
using Fluid;

namespace Dotto.Ai;

internal class PromptRenderer : IPromptRenderer
{
    private readonly FluidParser _parser = new();

    private static readonly TemplateOptions TemplateOptions = new()
    {
        MemberAccessStrategy = new UnsafeMemberAccessStrategy()
    };

    public async Task<string> RenderAsync(string template, object context, CancellationToken cancellationToken = default)
    {
        if (!_parser.TryParse(template, out var parsed, out var error))
            throw new InvalidOperationException($"Failed to parse prompt template: {error}");

        var fluidContext = new TemplateContext(context, TemplateOptions);
        return await parsed.RenderAsync(fluidContext);
    }
}
