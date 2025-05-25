using Dotto.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dotto.Infrastructure.Database.Configuration;

public class DownloadedMediaConfiguration : IEntityTypeConfiguration<DownloadedMediaRecord>
{
    public void Configure(EntityTypeBuilder<DownloadedMediaRecord> builder)
    {
        builder.Property(f => f.Id)
            .ValueGeneratedOnAdd();
        
        builder.Property(f => f.DownloadedFrom)
            .IsRequired();

        builder.Property(f => f.ChannelId)
            .IsRequired();
        
        builder.Property(f => f.MediaUrl)
            .IsRequired();

        builder.Property(f => f.InvokerId)
            .IsRequired();

        builder.Property(f => f.CreatedOn)
            .IsRequired();
    }
}