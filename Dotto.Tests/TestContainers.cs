using Dotto.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Tests;

public class TestContainers
{
    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder()
        .WithDatabase("dotto_testdb")
        .Build();
    
    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
        
        var collection = DependencyInjection.BuildNewServiceCollection();
        var provider = collection.BuildServiceProvider();
        
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