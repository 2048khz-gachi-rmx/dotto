using System.Linq.Expressions;
using Dotto.Application.Abstractions;
using Dotto.Application.Entities;
using Dotto.Application.InternalServices;
using Dotto.Common.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MockQueryable.NSubstitute;
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

    [Ignore("ngl still haven't figured out how to test EF calls via NSubstitute")]
    [Test]
    public async Task ShouldCacheFlags()
    {
        // Arrange
        var flag = new ChannelFlags(123);
        var mockDbSet = new[] { flag }.BuildMockDbSet();
        var mockDbContext = Substitute.For<IDottoDbContext>();
        mockDbContext.ChannelFlags.Returns(mockDbSet);
    
        var sut = new ChannelFlagsService(mockDbContext, TestDateTimeProvider, _mockCache);
        
        // Act
        for (var i = 0; i <= 5; i++)
            await sut.GetChannelFlags(123);
        
        // Assert
        mockDbSet.Received(1).FirstOrDefaultAsync(
            Arg.Any<Expression<Func<ChannelFlags, bool>>>(),
            Arg.Any<CancellationToken>());
    }
}