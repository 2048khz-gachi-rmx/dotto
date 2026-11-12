using System.Reflection;
using Dotto.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dotto.Infrastructure.Database;

public class DottoDbContext : DbContext
{
    public DbSet<ChannelFlags> ChannelFlags { get; init; }
    public DbSet<DownloadedMediaRecord> DownloadedMedia { get; init; }
    public DbSet<ConfigurationOverride> ConfigurationOverrides { get; init; }

    public DottoDbContext(DbContextOptions options) : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}