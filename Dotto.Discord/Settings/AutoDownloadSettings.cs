namespace Dotto.Discord.Settings;

public class AutoDownloadSettings
{
    /// <summary>URLs that are downloaded immediately when posted.</summary>
    public string[] Patterns { get; set; } = [];

    /// <summary>
    /// URLs whose original embed is worth keeping (e.g. x.com posts, where the text matters).
    /// These are not downloaded automatically; the bot adds a reaction instead and waits for a human to confirm.
    /// </summary>
    public string[] AmbiguousPatterns { get; set; } = [];
}