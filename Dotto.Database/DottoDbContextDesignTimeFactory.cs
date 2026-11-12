using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dotto.Infrastructure.Database;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>. The runtime provider is configured by
/// <see cref="DependencyInjection.AddDatabase"/>; tooling only needs a syntactically valid
/// connection string because it never connects.
/// </summary>
public sealed class DottoDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DottoDbContext>
{
    public DottoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DottoDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=dotto;Username=dotto;Password=dotto")
            .Options;

        return new DottoDbContext(options);
    }
}
