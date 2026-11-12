using Dotto.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dotto.Infrastructure.Database.Configuration;

public class ConfigurationOverrideConfiguration : IEntityTypeConfiguration<ConfigurationOverride>
{
    public void Configure(EntityTypeBuilder<ConfigurationOverride> builder)
    {
        builder.ToTable("ConfigurationOverrides");

        builder.Property(o => o.Id)
            .ValueGeneratedOnAdd();

        builder.Property(o => o.InstanceName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.Scope)
            .IsRequired();

        builder.Property(o => o.TargetId)
            .IsRequired();

        builder.Property(o => o.Key)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(o => o.Value)
            .IsRequired()
            .HasMaxLength(8000);

        builder.Property(o => o.CreatedOn)
            .IsRequired();

        builder.Property(o => o.UpdatedOn)
            .IsRequired();

        builder.HasIndex(o => new { o.InstanceName, o.Scope, o.TargetId, o.Key })
            .IsUnique();

        // The per-interaction lookup filters by instance and scope targets.
        builder.HasIndex(o => new { o.InstanceName, o.Scope, o.TargetId });
    }
}
