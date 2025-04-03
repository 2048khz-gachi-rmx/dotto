// New protected member declared in sealed type
// disabled because private methods can't be used by nUnit
#pragma warning disable CS0628

namespace Tests;

/// <summary>
/// A test run representing the whole application's lifetime,
/// ie, starts before the first test and ends after the last test
/// </summary>
[SetUpFixture]
public sealed class TestRun
{
    private static TestContainers? _testContainers;

    [OneTimeSetUp]
    protected async Task RunBeforeAnyTests()
    {
        _testContainers = new TestContainers();
        await _testContainers.InitializeAsync();
    }
    
    [OneTimeTearDown]
    protected async Task RunAfterAllTests()
    {
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