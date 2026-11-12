namespace Dotto.Ai.Prompts;

public class PromptSource
{
    public PromptType Type { get; set; }

    /// <summary>
    /// An embedded resource name, a file path, or — for <see cref="PromptType.Text"/> — the
    /// template text itself.
    /// </summary>
    public string Path { get; set; } = string.Empty;
}

public enum PromptType
{
    /// <summary>
    /// <see cref="PromptSource.Path"/> names an embedded resource (with or without the
    /// assembly and <c>Resources.</c> prefixes).
    /// </summary>
    Resource,

    /// <summary>
    /// <see cref="PromptSource.Path"/> is a file path, re-read when its mtime changes.
    /// </summary>
    File,

    /// <summary>
    /// <see cref="PromptSource.Path"/> is the template itself: no lookup, no file access. Useful
    /// for inline overrides such as <c>Ai:Prompts:ChatAssistant:Path</c>.
    /// </summary>
    Text
}
