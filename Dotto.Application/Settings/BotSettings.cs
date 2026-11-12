using System.ComponentModel.DataAnnotations;

namespace Dotto.Application.Settings;

/// <summary>
/// Identity of this bot instance and who may manage it.
/// </summary>
public class BotSettings
{
    /// <summary>
    /// Name used to partition configuration overrides when several bot instances
    /// share one database.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(100)]
    public string InstanceName { get; set; } = "dotto";

    /// <summary>
    /// User IDs allowed to manage configuration. Empty means "fall back to the
    /// Discord application owner"; team-owned applications must list owners here.
    /// </summary>
    public ulong[] OwnerIds { get; set; } = [];
}
