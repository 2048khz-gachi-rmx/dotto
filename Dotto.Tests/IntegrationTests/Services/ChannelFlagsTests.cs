using Dotto.Application.InternalServices;
using Dotto.Application.InternalServices.ChannelFlagsService;
using Dotto.Common.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using NSubstitute;
using Shouldly;

namespace Dotto.Tests.IntegrationTests.Services;

public class ChannelFlagsTests : TestDatabaseFixtureBase
{
    private readonly HybridCache _mockCache = Substitute.For<HybridCache>();
    
    [Test]
    public async Task ShouldCreateChannelFlag()
    {
        // Arrange
        var sut = new ChannelFlagsService(DbContext, TestDateTimeProvider, _mockCache);
        var now = TestDateTimeProvider.SetNow();
        
        // Act
        await sut.AddChannelFlag(102030, "testflag");
        
        // Assert
        var flags = await DbContext.ChannelFlags.ToListAsync();
        flags.Count.ShouldBe(1);
        flags.Single().ChannelId.ShouldBe<ulong>(102030);
        flags.Single().Flags.ShouldBe(["testflag"]);
        flags.Single().UpdatedOn.ShouldBe(now, TimeSpan.FromMilliseconds(1));
    }
    
    [Test]
    public async Task ShouldAppendChannelFlag()
    {
        // Arrange
        var sut = new ChannelFlagsService(DbContext, TestDateTimeProvider, _mockCache);
        var now = TestDateTimeProvider.SetNow();

        var flag = await ChannelFlagBuilder
            .WithChannelId(102030)
            .WithFlags(["pupa", "lupa"])
            .WithUpdatedOn(now.AddDays(-1))
            .GetAsync();
        
        // Act
        await sut.AddChannelFlag(flag.ChannelId, "booba");
        
        // Assert
        var flags = await DbContext.ChannelFlags.ToListAsync();
        flags.Count.ShouldBe(1);
        flags.Single().Flags.ShouldBe(["pupa", "lupa", "booba"]);
        flags.Single().UpdatedOn.ShouldBe(now, TimeSpan.FromMilliseconds(1));
    }
    
    [Test]
    public async Task ShouldNoopAddExistingChannelFlag()
    {
        // Arrange
        var sut = new ChannelFlagsService(DbContext, TestDateTimeProvider, _mockCache);
        var now = TestDateTimeProvider.SetNow();

        var flag = await ChannelFlagBuilder
            .WithChannelId(102030)
            .WithFlags(["pupa", "lupa"])
            .WithUpdatedOn(now.AddDays(-1))
            .GetAsync();
        
        // Act
        await sut.AddChannelFlag(flag.ChannelId, "lupa");
        
        // Assert
        var flags = await DbContext.ChannelFlags.ToListAsync();
        flags.Count.ShouldBe(1);
        flags.Single().Flags.ShouldBe(["pupa", "lupa"]);
        flags.Single().UpdatedOn.ShouldBe(now.AddDays(-1), TimeSpan.FromMilliseconds(1));
    }
    
    [Test]
    public async Task ShouldThrowLimitReached()
    {
        // Arrange
        var sut = new ChannelFlagsService(DbContext, TestDateTimeProvider, _mockCache);
        var now = TestDateTimeProvider.SetNow();

        var flag = await ChannelFlagBuilder
            .WithChannelId(102030)
            .WithFlags(Enumerable.Repeat("flag", Constants.ChannelFlags.MaxFlagsInChannel).ToList())
            .WithUpdatedOn(now.AddDays(-1))
            .GetAsync();
        
        // Act
        var shouldThrow = async () => await sut.AddChannelFlag(flag.ChannelId, "booba");
        
        // Assert
        shouldThrow.ShouldThrow<ArgumentOutOfRangeException>();
        
        var flags = await DbContext.ChannelFlags.ToListAsync();
        flags.Single().Flags.Count.ShouldBe(Constants.ChannelFlags.MaxFlagsInChannel);
        flags.Single().UpdatedOn.ShouldBe(now.AddDays(-1), TimeSpan.FromMilliseconds(1));
    }
    
    [Test]
    public async Task ShouldRemoveChannelFlag()
    {
        // Arrange
        var sut = new ChannelFlagsService(DbContext, TestDateTimeProvider, _mockCache);
        var now = TestDateTimeProvider.SetNow();

        var flag = await ChannelFlagBuilder
            .WithChannelId(102030)
            .WithFlags(["pupa", "lupa"])
            .WithUpdatedOn(now.AddDays(-1))
            .GetAsync();
        
        // Act
        await sut.RemoveChannelFlag(flag.ChannelId, "lupa");
        
        // Assert
        var flags = await DbContext.ChannelFlags.ToListAsync();
        flags.Count.ShouldBe(1);
        flags.Single().Flags.ShouldBe(["pupa"]);
        flags.Single().UpdatedOn.ShouldBe(now, TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public async Task ShouldCacheFlags()
    {
        // Arrange
        var mockDbContext = Substitute.For<IDottoDbContext>();
        var sut = new ChannelFlagsService(mockDbContext, TestDateTimeProvider, _mockCache);
        var now = TestDateTimeProvider.SetNow();

        var flag = await ChannelFlagBuilder
            .WithChannelId(102030)
            .WithFlags(["pupa", "lupa"])
            .WithUpdatedOn(now.AddDays(-1))
            .GetAsync();
        
        // Act
        for (var i = 0; i <= 5; i++)
            await sut.GetChannelFlags(flag.ChannelId);
        
        // Assert
        mockDbContext.ChannelFlags.Received(1);
    }
}