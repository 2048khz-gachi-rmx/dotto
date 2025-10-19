using Dotto.Tests.Mocks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NetCord;
using NetCord.Gateway;

namespace Dotto.Tests;

/// <summary>
/// A fixture base, representing a block of tests
/// </summary>
public abstract class TestFixtureBase
{
    private ServiceProvider _rootProvider;
    private IServiceScope? _scope;
    
    protected IServiceProvider ServiceProvider => _scope!.ServiceProvider;
    
    protected TestDateTimeProvider TestDateTimeProvider => ServiceProvider.GetRequiredService<TestDateTimeProvider>();
    protected IMediator Mediator => ServiceProvider.GetRequiredService<IMediator>();
    
    protected readonly GatewayClient NetCordClient = new(new BotToken("ODAzMzc3MjcwODc4MTA5NzI2.tHis.IS.not.A.ReAl.tOkeN"));
    
    [SetUp]
    public virtual Task Setup()
    {
        _rootProvider = BuildServiceCollection().BuildServiceProvider();
        NewScope();
        
        return Task.CompletedTask;
    }
    
    [TearDown]
    public virtual async Task TearDown()
    {
        await _rootProvider.DisposeAsync();
        
        _scope?.Dispose();
    }

    protected virtual ServiceCollection BuildServiceCollection()
    {
        return DependencyInjection.BuildNewServiceCollection();
    }
    
    protected void NewScope()
    {
        _scope?.Dispose();
        _scope = _rootProvider.CreateScope();
    }
}