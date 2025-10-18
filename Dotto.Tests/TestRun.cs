using DotNet.Testcontainers.Builders;

namespace Dotto.Tests;

/// <summary>
/// A test run representing the whole application's lifetime,
/// ie, starts before the first test and ends after the last test
/// </summary>
[SetUpFixture]
public class TestRun
{
    private static TestContainers? _testContainers;
    
    private static readonly SemaphoreSlim InitializeLock = new(1);

    /// <summary>
    /// Ensures the test containers are spun up and available
    /// </summary>
    public static async Task EnsureInitialized()
    {
        if (_testContainers != null)
            return;

        await InitializeLock.WaitAsync();
        
        if (_testContainers != null)
            return;

        try
        {
            var containers = new TestContainers();
            await containers.InitializeAsync();
            _testContainers = containers;
        }
        catch (DockerUnavailableException ex)
        {
            Assert.Ignore("TestContainers could not spin up due to Docker being unavailable.\n" + ex.Message);
        }
        finally
        {
            InitializeLock.Release();
        }
    }
    
    [OneTimeTearDown]
    protected async Task RunAfterAllTests()
    {
        if (_testContainers == null)
            return;
        
        await _testContainers!.DisposeAsync();
        _testContainers = null;
    }

    public static string GetConnectionString()
    {
        if (_testContainers == null)
            throw new ArgumentException("container not spun up; not supposed to ever happen");

        return _testContainers.GetConnectionString();
    }
}