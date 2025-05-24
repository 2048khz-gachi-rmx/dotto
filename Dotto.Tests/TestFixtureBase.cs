using System.Diagnostics;
using Dotto.Application.InternalServices.ChannelFlagsService;
using Dotto.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tests.Builders;
using Tests.Mocks;

namespace Tests;

/// <summary>
/// A fixture base, representing a block of tests
/// </summary>
public abstract class TestFixtureBase
{
    private ServiceProvider _rootProvider;
    private IServiceScope? _scope;
    
    protected IServiceProvider ServiceProvider => _scope!.ServiceProvider;
    
    protected TestDateTimeProvider TestDateTimeProvider => ServiceProvider.GetRequiredService<TestDateTimeProvider>();
    protected DottoDbContext DbContext => ServiceProvider.GetRequiredService<DottoDbContext>();
    protected IMediator Mediator => ServiceProvider.GetRequiredService<IMediator>();
    
    protected ChannelFlagsService ChannelFlags => ServiceProvider.GetRequiredService<ChannelFlagsService>();
    
    protected ChannelFlagBuilder ChannelFlagBuilder => ServiceProvider.GetRequiredService<ChannelFlagBuilder>();
    
    [SetUp]
    public Task Setup()
    {
        _rootProvider = DependencyInjection.BuildNewServiceProvider();
        NewScope();
        
        return Task.CompletedTask;
    }
    
    [TearDown]
    public async Task TearDown()
    {
        await ClearDatabase();
        await _rootProvider.DisposeAsync();
        
        _scope?.Dispose();
    }
    
    protected void NewScope()
    {
        _scope?.Dispose();
        _scope = _rootProvider.CreateScope();
    }

    private async Task ClearDatabase()
    {
        // sidenote: goddamn deepseek is good at this, just straight up fed me
        // both the truncate approach and the drop approach, fixing it when asked
        // (like how you cant drop with open connections)
        
        var sw = new Stopwatch();
        sw.Start();
        
        var db = _rootProvider.GetRequiredService<DottoDbContext>();
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
        
        var db = _rootProvider.GetRequiredService<DottoDbContext>();
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