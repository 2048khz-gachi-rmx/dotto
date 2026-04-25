using System.Collections.Immutable;
using Dotto.Application.Abstractions;
using Dotto.Application.Entities;
using Dotto.Common.DateTimeProvider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Dotto.Application.InternalServices;

public interface IChannelFlagsService
{
    Task<bool> AddChannelFlag(ulong channelId, string flagName, CancellationToken ct = default);
    Task<bool> RemoveChannelFlag(ulong channelId, string flagName, CancellationToken ct = default);
    ValueTask<ImmutableList<string>> GetChannelFlags(ulong channelId, CancellationToken ct = default);
    Task<DateTime> UpdateCachedFlags(DateTime lastUpdate);
}

public class ChannelFlagsService(
    IDottoDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    HybridCache hybridCache
    ) : IChannelFlagsService
{
    public async Task<bool> AddChannelFlag(ulong channelId, string flagName, CancellationToken ct = default)
    {
        var flags = await dbContext.ChannelFlags.FirstOrDefaultAsync(f => f.ChannelId == channelId, ct);

        if (flags == null)
        {
            flags = new ChannelFlags(channelId);
            dbContext.ChannelFlags.Add(flags);
        }

        var now = dateTimeProvider.UtcNow;

        if (!flags.AddFlag(flagName, now))
            return false;

        await dbContext.SaveChangesAsync(ct);
        
        // don't try to get chummy by keeping cache more-or-less-updated, otherwise we introduce various data races and stale data...
        await hybridCache.RemoveAsync(GetCacheKey(channelId), cancellationToken: CancellationToken.None);

        return true;
    }

    public async Task<bool> RemoveChannelFlag(ulong channelId, string flagName, CancellationToken ct = default)
    {
        var flags = await dbContext.ChannelFlags.FirstOrDefaultAsync(f => f.ChannelId == channelId, ct);
        var now = dateTimeProvider.UtcNow;

        if (flags == null || !flags.RemoveFlag(flagName, now))
            return false;

        await dbContext.SaveChangesAsync(ct);
        await hybridCache.RemoveAsync(GetCacheKey(channelId), cancellationToken: CancellationToken.None);

        return true;
    }

    public ValueTask<ImmutableList<string>> GetChannelFlags(ulong channelId, CancellationToken ct = default)
        => GetCachedFlagsAsync(channelId, ct);

    public async Task<DateTime> UpdateCachedFlags(DateTime lastUpdate)
    {
        var changed = await dbContext.ChannelFlags.AsNoTracking()
            .Where(f => f.UpdatedOn > lastUpdate)
            .Select(f => new { f.ChannelId, f.UpdatedOn })
            .ToListAsync();

        var maxUpdate = lastUpdate;
        var tasks = new List<Task>();

        foreach (var f in changed)
        {
            maxUpdate = f.UpdatedOn > maxUpdate ? f.UpdatedOn : maxUpdate;

            // Potentially outdated flags in cache; invalidate it, so the next time the flags are needed,
            // we do a DB roundtrip and get truth data
            tasks.Add(hybridCache.RemoveAsync(GetCacheKey(f.ChannelId)).AsTask());
        }

        await Task.WhenAll(tasks);

        return maxUpdate;
    }
    
    private static string GetCacheKey(ulong channelId)
        => $"channelflags-{channelId}";

    private ValueTask<ImmutableList<string>> GetCachedFlagsAsync(ulong channelId, CancellationToken ct)
    {
        return hybridCache.GetOrCreateAsync<ImmutableList<string>>(GetCacheKey(channelId),
            async token =>
            {
                var entity = await dbContext.ChannelFlags.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.ChannelId == channelId, token);

                return entity?.Flags.ToImmutableList() ?? ImmutableList<string>.Empty;
            },
            new HybridCacheEntryOptions() { LocalCacheExpiration = TimeSpan.FromMinutes(5) },
            cancellationToken: ct);
    }
}