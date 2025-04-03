using System.Reflection;
using Dotto.Application.Entities;
using Dotto.Application.InternalServices;
using Microsoft.EntityFrameworkCore;

namespace Dotto.Infrastructure.Database;

public class DottoDbContext : DbContext, IDottoDbContext
{
    public DbSet<ChannelFlags> ChannelFlags { get; init; }
    
    public DottoDbContext(DbContextOptions options) : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}