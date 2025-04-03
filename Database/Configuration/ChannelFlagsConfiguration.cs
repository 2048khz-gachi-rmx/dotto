using Dotto.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dotto.Infrastructure.Database.Configuration;

public class ChannelFlagsConfiguration : IEntityTypeConfiguration<ChannelFlags>
{
    public void Configure(EntityTypeBuilder<ChannelFlags> builder)
    {
        builder.HasKey(f => f.ChannelId);

        builder.Property(f => f.Flags);
        
        // we'll be polling by UpdatedOn, so we want to make sure it's at least gonna be fast
        builder.HasIndex(f => f.UpdatedOn);
    }
}