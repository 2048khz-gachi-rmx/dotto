﻿using Dotto.Application.Entities;
using Dotto.Common.DateTimeProvider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Dotto.Application.InternalServices.ChannelFlagsService;

// I see no good reason to mock this, so no interfaces
// Also, while i _could_ do fancy stuff like not selecting the entity to avoid an extra roundtrip,
// flag updates are going to be so rare that it just won't be worth it
public class ChannelFlagsService(
    IDottoDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    HybridCache hybridCache
    )
{
    public async Task<bool> AddChannelFlag(ulong channelId, string flagName, CancellationToken ct = default)
    {
        var flags = await dbContext.ChannelFlags.FirstOrDefaultAsync(f => f.ChannelId == channelId, ct);

        if (flags == null)
        {
            flags = new ChannelFlags() { ChannelId = channelId };
            dbContext.ChannelFlags.Add(flags);
        }
        
        var now = dateTimeProvider.UtcNow;
        
        if (!flags.AddFlag(flagName, now))
            return false;

        await dbContext.SaveChangesAsync(ct);
        await hybridCache.SetAsync($"channelflags-{channelId}", flags.Flags);
        
        return true;
    }
    
    public async Task<bool> RemoveChannelFlag(ulong channelId, string flagName, CancellationToken ct = default)
    {
        var flags = await dbContext.ChannelFlags.FirstOrDefaultAsync(f => f.ChannelId == channelId, ct);
        var now = dateTimeProvider.UtcNow;
        
        if (flags == null || !flags.RemoveFlag(flagName, now))
            return false;

        await dbContext.SaveChangesAsync(ct);
        await hybridCache.SetAsync($"channelflags-{channelId}", flags.Flags, cancellationToken: ct);
        
        return true;
    }

    private string GetCacheKey(ulong channelId) => $"channelflags-{channelId}";
    
    public ValueTask<IList<string>?> GetChannelFlags(ulong channelId, CancellationToken ct = default)
    {
        return hybridCache.GetOrCreateAsync<IList<string>?>(GetCacheKey(channelId),
            async token =>
            { 
                return (await dbContext.ChannelFlags
                    .FirstOrDefaultAsync(f => f.ChannelId == channelId, token))
                    ?.Flags;
            },
            cancellationToken: ct);
    }
    
    public async Task<DateTime> UpdateCachedFlags(DateTime lastUpdate)
    {
        var newFlags = await dbContext.ChannelFlags
            .Where(f => f.UpdatedOn > lastUpdate)
            .ToListAsync();

        var maxUpdate = lastUpdate;
        
        newFlags.ForEach(f =>
        {
            maxUpdate = f.UpdatedOn > maxUpdate ? f.UpdatedOn : maxUpdate;
            hybridCache.SetAsync(GetCacheKey(f.ChannelId), f.Flags);
        });

        return maxUpdate;
    }
}