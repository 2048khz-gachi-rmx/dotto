using Dotto.Common.Constants;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Tests.Tests.Services;

public class ChannelFlagsTests : TestFixtureBase
{
    [Test]
    public async Task ShouldCreateChannelFlag()
    {
        // Arrange
        var now = TestDateTimeProvider.SetNow();
        
        // Act
        await ChannelFlags.AddChannelFlag(102030, "testflag");
        
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
        var now = TestDateTimeProvider.SetNow();

        var flag = await ChannelFlagBuilder
            .WithChannelId(102030)
            .WithFlags(["pupa", "lupa"])
            .WithUpdatedOn(now.AddDays(-1))
            .GetAsync();
        
        // Act
        NewScope();
        await ChannelFlags.AddChannelFlag(flag.ChannelId, "booba");
        
        // Assert
        NewScope();
        var flags = await DbContext.ChannelFlags.ToListAsync();
        flags.Count.ShouldBe(1);
        flags.Single().Flags.ShouldBe(["pupa", "lupa", "booba"]);
        flags.Single().UpdatedOn.ShouldBe(now, TimeSpan.FromMilliseconds(1));
    }
    
    [Test]
    public async Task ShouldNoopAddExistingChannelFlag()
    {
        // Arrange
        var now = TestDateTimeProvider.SetNow();

        var flag = await ChannelFlagBuilder
            .WithChannelId(102030)
            .WithFlags(["pupa", "lupa"])
            .WithUpdatedOn(now.AddDays(-1))
            .GetAsync();
        
        // Act
        NewScope();
        await ChannelFlags.AddChannelFlag(flag.ChannelId, "lupa");
        
        // Assert
        NewScope();
        var flags = await DbContext.ChannelFlags.ToListAsync();
        flags.Count.ShouldBe(1);
        flags.Single().Flags.ShouldBe(["pupa", "lupa"]);
        flags.Single().UpdatedOn.ShouldBe(now.AddDays(-1), TimeSpan.FromMilliseconds(1));
    }
    
    [Test]
    public async Task ShouldThrowLimitReached()
    {
        // Arrange
        var now = TestDateTimeProvider.SetNow();

        var flag = await ChannelFlagBuilder
            .WithChannelId(102030)
            .WithFlags(Enumerable.Repeat("flag", Constants.ChannelFlags.MaxFlagsInChannel).ToList())
            .WithUpdatedOn(now.AddDays(-1))
            .GetAsync();
        
        // Act
        NewScope();
        var shouldThrow = async () => await ChannelFlags.AddChannelFlag(flag.ChannelId, "booba");
        
        // Assert
        NewScope();
        shouldThrow.ShouldThrow<ArgumentOutOfRangeException>();
        
        var flags = await DbContext.ChannelFlags.ToListAsync();
        flags.Single().Flags.Count.ShouldBe(Constants.ChannelFlags.MaxFlagsInChannel);
        flags.Single().UpdatedOn.ShouldBe(now.AddDays(-1), TimeSpan.FromMilliseconds(1));
    }
    
    [Test]
    public async Task ShouldRemoveChannelFlag()
    {
        // Arrange
        var now = TestDateTimeProvider.SetNow();

        var flag = await ChannelFlagBuilder
            .WithChannelId(102030)
            .WithFlags(["pupa", "lupa"])
            .WithUpdatedOn(now.AddDays(-1))
            .GetAsync();
        
        // Act
        NewScope();
        await ChannelFlags.RemoveChannelFlag(flag.ChannelId, "lupa");
        
        // Assert
        NewScope();
        var flags = await DbContext.ChannelFlags.ToListAsync();
        flags.Count.ShouldBe(1);
        flags.Single().Flags.ShouldBe(["pupa"]);
        flags.Single().UpdatedOn.ShouldBe(now, TimeSpan.FromMilliseconds(1));
    }

    [Ignore("how the hell do i test this, i can't count sql queries")]
    [Test]
    public async Task ShouldCacheFlags()
    {
        // Arrange
        var now = TestDateTimeProvider.SetNow();

        var flag = await ChannelFlagBuilder
            .WithChannelId(102030)
            .WithFlags(["pupa", "lupa"])
            .WithUpdatedOn(now.AddDays(-1))
            .GetAsync();
        
        // Act
        NewScope();
        await ChannelFlags.GetChannelFlags(flag.ChannelId);
        await ChannelFlags.GetChannelFlags(flag.ChannelId);
        
        // Assert
        NewScope();
    }
}