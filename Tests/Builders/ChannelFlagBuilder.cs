using Dotto.Application.Entities;
using Dotto.Common.DateTimeProvider;
using Dotto.Infrastructure.Database;
using Tests.Mocks;

namespace Tests.Builders;

public class ChannelFlagBuilder(DottoDbContext dbContext, IDateTimeProvider dateTimeProvider)
{
    private ulong? _channelId;
    private IList<string>? _flags;
    private DateTime? _updatedOn;

    private ulong _counter = 0;
    
    public async Task<ChannelFlags> GetAsync()
    {
        var flag = new ChannelFlags()
        {
            ChannelId = _channelId ?? ++_counter,
            Flags = _flags ?? [],
            UpdatedOn = _updatedOn ?? dateTimeProvider.UtcNow
        };

        dbContext.ChannelFlags.Add(flag);
        await dbContext.SaveChangesAsync();

        return flag;
    }
    
    public ChannelFlagBuilder WithChannelId(ulong channelId)
    {
        _channelId = channelId;
        return this;
    }

    public ChannelFlagBuilder WithFlags(IList<string> flags)
    {
        _flags = flags;
        return this;
    }
    
    public ChannelFlagBuilder WithUpdatedOn(DateTime updatedOn)
    {
        _updatedOn = updatedOn;
        return this;
    }
}