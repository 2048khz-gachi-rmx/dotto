using System.Reflection;
using Dotto.Application.Abstractions;
using Dotto.Application.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dotto.Infrastructure.Database;

public class DottoDbContext : DbContext, IDottoDbContext
{
    public DbSet<ChannelFlags> ChannelFlags { get; init; }
    public DbSet<DownloadedMediaRecord> DownloadedMedia { get; init; }

    public DottoDbContext(DbContextOptions options) : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}