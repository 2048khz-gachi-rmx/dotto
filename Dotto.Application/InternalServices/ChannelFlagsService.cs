using System.Collections.Immutable;
using Dotto.Application.Abstractions;
using Dotto.Application.Entities;
using Dotto.Common.DateTimeProvider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Dotto.Application.InternalServices;

// I see no good reason to mock this, so no interfaces
public class ChannelFlagsService(
    IDottoDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    HybridCache hybridCache
    )
{
    public async Task<bool> AddChannelFlag(ulong channelId, string flagName, CancellationToken ct = default)
    {
        // While i _could_ do fancy stuff like not selecting the entity to avoid an extra roundtrip,
        // flag updates are going to be so rare that it just won't be worth it.
        // Plus it's logic moved to the DB, which sucks (checking if a channel reached max flags?)
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
        await hybridCache.SetAsync($"channelflags-{channelId}", flags.Flags, cancellationToken: CancellationToken.None);
        
        return true;
    }
    
    public async Task<bool> RemoveChannelFlag(ulong channelId, string flagName, CancellationToken ct = default)
    {
        var flags = await dbContext.ChannelFlags.FirstOrDefaultAsync(f => f.ChannelId == channelId, ct);
        var now = dateTimeProvider.UtcNow;
        
        if (flags == null || !flags.RemoveFlag(flagName, now))
            return false;

        await dbContext.SaveChangesAsync(ct);
        await hybridCache.SetAsync($"channelflags-{channelId}", flags.Flags, cancellationToken: CancellationToken.None);
        
        return true;
    }

    private string GetCacheKey(ulong channelId) => $"channelflags-{channelId}";
    
    public ValueTask<IImmutableList<string>> GetChannelFlags(ulong channelId, CancellationToken ct = default)
    {
        return hybridCache.GetOrCreateAsync<IImmutableList<string>>(GetCacheKey(channelId),
            async token =>
            { 
                return (await dbContext.ChannelFlags
                    .FirstOrDefaultAsync(f => f.ChannelId == channelId, token))
                    ?.Flags.ToImmutableList() ?? ImmutableList<string>.Empty;
            },
            cancellationToken: ct);
    }
    
    public async Task<DateTime> UpdateCachedFlags(DateTime lastUpdate)
    {
        var newFlags = await dbContext.ChannelFlags
            .Where(f => f.UpdatedOn > lastUpdate)
            .ToListAsync();

        var maxUpdate = lastUpdate;
        var tasks = new List<Task>();
        
        newFlags.ForEach(f =>
        {
            maxUpdate = f.UpdatedOn > maxUpdate ? f.UpdatedOn : maxUpdate;
            var task = hybridCache.SetAsync(GetCacheKey(f.ChannelId), f.Flags);
            
            if (!task.IsCompleted)
                tasks.Add(task.AsTask()); // ValueTask instances must be awaited blah blah oh shut up
        });

        await Task.WhenAll(tasks);

        return maxUpdate;
    }
}