namespace Dotto.Ai.Settings;

public class PromptSource
{
    public PromptType Type { get; set; }
    public string Path { get; set; } = string.Empty;
}

public enum PromptType
{
    Resource,
    File
}
