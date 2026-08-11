using Dotto.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Dotto.Tests;

public class TestContainers
{
    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("dotto_testdb")
        .WithCommand(
            "-c", "shared_buffers=16MB",
            "-c", "max_connections=5",
            "-c", "wal_level=minimal",
            "-c", "max_wal_senders=0",
            "-c", "fsync=off",
            "-c", "synchronous_commit=off",
            "-c", "full_page_writes=off",
            "-c", "autovacuum=off"
        )
        .Build();
    
    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
        
        var provider = DependencyInjection.BuildNewServiceCollection()
            .AddDatabase(GetConnectionString())
            .BuildServiceProvider();
        
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DottoDbContext>();
        await dbContext.Database.MigrateAsync();
        await provider.DisposeAsync();
    }

    public Task DisposeAsync()
    {
        return _postgreSqlContainer.DisposeAsync().AsTask();
    }

    public string GetConnectionString()
    {
        return _postgreSqlContainer.GetConnectionString();
    }
}