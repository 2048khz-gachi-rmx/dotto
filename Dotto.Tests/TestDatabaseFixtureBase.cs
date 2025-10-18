using System.Diagnostics;
using Dotto.Infrastructure.Database;
using Dotto.Tests.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Dotto.Tests;

/// <summary>
/// A fixture base, representing a block of tests, that provides a database
/// </summary>
public abstract class TestDatabaseFixtureBase : TestFixtureBase
{
    protected DottoDbContext DbContext => ServiceProvider.GetRequiredService<DottoDbContext>();
    protected ChannelFlagBuilder ChannelFlagBuilder => ServiceProvider.GetRequiredService<ChannelFlagBuilder>();

    private bool _initialized;
    
    [SetUp]
    public override async Task Setup()
    {
        await TestRun.EnsureInitialized();
        await base.Setup();
        _initialized = true;
    }
    
    [TearDown]
    public override async Task TearDown()
    {
        if (!_initialized)
            return;
        
        await ClearDatabase();
        await base.TearDown();
    }

    protected override ServiceCollection BuildServiceCollection()
    {
        var collection = base.BuildServiceCollection();
        collection.AddDatabase(TestRun.GetConnectionString());

        return collection;
    }

    private async Task ClearDatabase()
    {
        var sw = new Stopwatch();
        sw.Start();
        
        var db = ServiceProvider.GetService<DottoDbContext>();
        
        if (db == null)
            return;
        
        var tableNames = db.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Distinct()
            .ToList();

#pragma warning disable EF1002
        foreach (var tableName in tableNames)
        {
            await db.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE \"{tableName}\" CASCADE;");
        }
#pragma warning restore EF1002
        
        await db.SaveChangesAsync();
        
        sw.Stop();
        
        Debug.WriteLine("DB cleared in {0}", sw.ElapsedMilliseconds);
    }
    
    // Clearing the table is >100x faster than dropping then remigrating it
    public async Task RecreateDatabase()
    {
        var sw = new Stopwatch();
        sw.Start();
        
        var db = ServiceProvider.GetRequiredService<DottoDbContext>();
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                SELECT pg_terminate_backend(pg_stat_activity.pid)
                FROM pg_stat_activity
                WHERE pg_stat_activity.datname = 'dotto_testdb';
                DROP DATABASE dotto_testdb;
            ");
        }
        catch (PostgresException ex)
        {
            // ignore self-own
            if (!ex.Message.StartsWith("57P01")) throw;
        }
        
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        sw.Stop();
        
        Debug.WriteLine("DB recreated in {0}", sw.ElapsedMilliseconds);
    }
}