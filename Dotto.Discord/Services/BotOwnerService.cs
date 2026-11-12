using Dotto.Application.Configuration;
using Dotto.Application.Settings;
using Dotto.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;

namespace Dotto.Discord.Services;

/// <summary>
/// Resolves the Discord application's owner, with an abstraction so the owner service can
/// be tested without a live Discord API.
/// </summary>
public interface IApplicationOwnerProvider
{
    /// <summary>Returns the application owner's user ID, or null for team-owned applications.</summary>
    Task<ulong?> GetOwnerIdAsync(CancellationToken ct = default);
}

internal sealed class DiscordApplicationOwnerProvider(RestClient client) : IApplicationOwnerProvider
{
    public async Task<ulong?> GetOwnerIdAsync(CancellationToken ct = default)
    {
        var application = await client.GetCurrentBotApplicationInformationAsync(
            cancellationToken: ct);

        // Team-owned applications have no single owner; teams must configure Bot:OwnerIds.
        if (application.Team != null)
            return null;

        return application.Owner?.Id;
    }
}

public interface IBotOwnerService
{
    Task<bool> IsOwnerAsync(ulong userId, CancellationToken ct = default);
}

internal sealed class BotOwnerService(
    IOptions<BotSettings> botSettings,
    IApplicationOwnerProvider applicationOwnerProvider,
    ILogger<BotOwnerService> logger) : IBotOwnerService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _resolvedFallback;
    private ulong? _fallbackOwnerId;

    public async Task<bool> IsOwnerAsync(ulong userId, CancellationToken ct = default)
    {
        var configured = botSettings.Value.OwnerIds;

        if (configured.Length > 0)
            return configured.Contains(userId);

        var ownerId = await GetFallbackOwnerIdAsync(ct);
        return ownerId != null && ownerId.Value == userId;
    }

    private async Task<ulong?> GetFallbackOwnerIdAsync(CancellationToken ct)
    {
        if (_resolvedFallback)
            return _fallbackOwnerId;

        await _lock.WaitAsync(ct);
        try
        {
            if (_resolvedFallback)
                return _fallbackOwnerId;

            try
            {
                _fallbackOwnerId = await applicationOwnerProvider.GetOwnerIdAsync(ct);
                _resolvedFallback = true;
                return _fallbackOwnerId;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fail closed: an unavailable API must not open configuration management.
                logger.LogError(
                    ex,
                    "Could not resolve the Discord application owner; configuration management is denied. " +
                    "Set Bot:OwnerIds to avoid this lookup.");
                return null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
