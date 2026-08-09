using System.Reflection;
using Dotto.Ai.Abstractions;
using Dotto.Ai.Settings;

namespace Dotto.Ai;

internal class PromptTemplate(PromptSource source) : IPromptTemplate
{
    private readonly Assembly _assembly = typeof(PromptTemplate).Assembly;
    private readonly string _assemblyPrefix = typeof(PromptTemplate).Assembly.GetName().Name! + ".";
    private string? _cachedContent;
    private DateTime? _lastWriteTime;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedContent != null && source.Type != PromptType.File)
            return _cachedContent;

        if (source.Type == PromptType.Resource)
            return await GetResourceAsync(cancellationToken);

        return await GetFileAsync(cancellationToken);
    }

    private async Task<string> GetResourceAsync(CancellationToken ct)
    {
        if (_cachedContent != null)
            return _cachedContent;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedContent != null)
                return _cachedContent;

            var resourceName = PathToResourceName(source.Path);
            await using var stream = _assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException(
                    $"Embedded resource '{resourceName}' not found. " +
                    $"Available: [{string.Join(", ", _assembly.GetManifestResourceNames())}]");

            using var reader = new StreamReader(stream);
            _cachedContent = await reader.ReadToEndAsync(ct);
            return _cachedContent;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> GetFileAsync(CancellationToken ct)
    {
        var lastWrite = File.GetLastWriteTimeUtc(source.Path);

        if (_cachedContent != null && _lastWriteTime == lastWrite)
            return _cachedContent;

        await _lock.WaitAsync(ct);
        try
        {
            var currentWrite = File.GetLastWriteTimeUtc(source.Path);
            if (_cachedContent != null && _lastWriteTime == currentWrite)
                return _cachedContent;

            _cachedContent = await File.ReadAllTextAsync(source.Path, ct);
            _lastWriteTime = currentWrite;
            return _cachedContent;
        }
        finally
        {
            _lock.Release();
        }
    }

    private string PathToResourceName(string path)
    {
        var normalized = path.Replace('/', '.').Replace('\\', '.');

        if (normalized.StartsWith(_assemblyPrefix))
            return normalized;

        // Paths relative to Resources/ — e.g. "ChatAssistant.liquid" → "Resources.ChatAssistant.liquid"
        const string resourcesPrefix = "Resources.";
        if (!normalized.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = resourcesPrefix + normalized;

        return _assemblyPrefix + normalized;
    }
}
