using Dotto.Application.Settings;
using Dotto.Common.DateTimeProvider;
using Dotto.Domain.Entities;
using Dotto.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dotto.Application.Configuration;

internal sealed class ConfigurationOverrideStore(
    DottoDbContext dbContext,
    IOptions<BotSettings> botSettings,
    IDateTimeProvider dateTimeProvider) : IConfigurationOverrideStore
{
    public string InstanceName => botSettings.Value.InstanceName;

    public async Task<IReadOnlyList<ConfigurationOverride>> GetEffectiveAsync(
        ulong? guildId, ulong channelId, ulong userId, CancellationToken ct = default)
    {
        var instance = InstanceName;

        var query = dbContext.ConfigurationOverrides
            .AsNoTracking()
            .Where(o => o.InstanceName == instance);

        query = query.Where(o =>
            o.Scope == ConfigurationScope.Global
            || (guildId != null && o.Scope == ConfigurationScope.Guild && o.TargetId == guildId.Value)
            || (channelId != 0 && o.Scope == ConfigurationScope.Channel && o.TargetId == channelId)
            || (userId != 0 && o.Scope == ConfigurationScope.User && o.TargetId == userId));

        return await query
            .OrderBy(o => o.Scope)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ConfigurationOverride>> ListAsync(
        ConfigurationTarget target, CancellationToken ct = default)
    {
        var instance = InstanceName;
        var (scope, targetId) = (target.Scope, target.TargetId);

        return await dbContext.ConfigurationOverrides
            .AsNoTracking()
            .Where(o => o.InstanceName == instance && o.Scope == scope && o.TargetId == targetId)
            .OrderBy(o => o.Key)
            .ToListAsync(ct);
    }

    public Task<ConfigurationOverride?> GetAsync(
        ConfigurationTarget target, string key, CancellationToken ct = default)
    {
        var instance = InstanceName;
        var normalizedKey = NormalizeKey(key);
        var (scope, targetId) = (target.Scope, target.TargetId);

        return dbContext.ConfigurationOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(
                o => o.InstanceName == instance
                     && o.Scope == scope
                     && o.TargetId == targetId
                     && o.Key == normalizedKey,
                ct);
    }

    public async Task<bool> SetAsync(
        ConfigurationTarget target, string key, string value, CancellationToken ct = default)
    {
        var instance = InstanceName;
        var normalizedKey = NormalizeKey(key);
        var (scope, targetId) = (target.Scope, target.TargetId);
        var now = dateTimeProvider.UtcNow;

        var existing = await dbContext.ConfigurationOverrides
            .FirstOrDefaultAsync(
                o => o.InstanceName == instance
                     && o.Scope == scope
                     && o.TargetId == targetId
                     && o.Key == normalizedKey,
                ct);

        if (existing != null)
        {
            existing.Value = value;
            existing.UpdatedOn = now;
            await dbContext.SaveChangesAsync(ct);
            return true;
        }

        dbContext.ConfigurationOverrides.Add(new ConfigurationOverride
        {
            InstanceName = instance,
            Scope = scope,
            TargetId = targetId,
            Key = normalizedKey,
            Value = value,
            CreatedOn = now,
            UpdatedOn = now
        });

        await dbContext.SaveChangesAsync(ct);
        return false;
    }

    public async Task<bool> DeleteAsync(
        ConfigurationTarget target, string key, CancellationToken ct = default)
    {
        var instance = InstanceName;
        var normalizedKey = NormalizeKey(key);
        var (scope, targetId) = (target.Scope, target.TargetId);

        var deleted = await dbContext.ConfigurationOverrides
            .Where(o => o.InstanceName == instance
                        && o.Scope == scope
                        && o.TargetId == targetId
                        && o.Key == normalizedKey)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }

    private static string NormalizeKey(string key) => key.Trim().ToLowerInvariant();
}
