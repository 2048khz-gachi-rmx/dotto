using Dotto.Application.Modules.ChannelFlags;
using Dotto.Common.Constants;
using NetCord.Rest;
using Shouldly;

namespace Dotto.Tests.IntegrationTests.Commands;

public class ChannelFlagCommandsTests : TestDatabaseFixtureBase
{
    [Test]
    public async Task ShouldAddFlag()
    {
        // Arrange
        await ChannelFlagBuilder
            .WithChannelId(123)
            .WithFlags(["preflag"])
            .GetAsync();
        
        // Send
        var msg = await Mediator.Send(new AddFlagRequest<InteractionMessageProperties>
        {
            ChannelId = 123,
            FlagName = "newflag"
        });
        
        // Assert
        msg.Content.ShouldStartWith("Flag added!");
        msg.Content.ShouldNotBeNull().ShouldContain("preflag");
        msg.Content.ShouldNotBeNull().ShouldContain("newflag");
    }
    
    [Test]
    public async Task ShouldReplyAddLimitReached()
    {
        // Arrange
        var now = TestDateTimeProvider.SetNow();

        await ChannelFlagBuilder
            .WithChannelId(123)
            .WithFlags(Enumerable.Repeat("flag", Constants.ChannelFlags.MaxFlagsInChannel).ToList())
            .WithUpdatedOn(now.AddDays(-1))
            .GetAsync();
        
        // Send
        NewScope();
        var shouldThrow = async () => await Mediator.Send(new AddFlagRequest<InteractionMessageProperties>
        {
            ChannelId = 123,
            FlagName = "newflag"
        });
        
        // Assert
        NewScope();
        (await shouldThrow.ShouldThrowAsync<ArgumentOutOfRangeException>())
            .Message.ShouldStartWith("Channel reached the flag limit");
    }
    
    [Test]
    public async Task ShouldRemoveFlag()
    {
        // Arrange
        await ChannelFlagBuilder
            .WithChannelId(123)
            .WithFlags(["keepFlag", "removeFlag"])
            .GetAsync();
        
        // Send
        var msg = await Mediator.Send(new RemoveFlagRequest<InteractionMessageProperties>
        {
            ChannelId = 123,
            FlagName = "removeFlag"
        });
        
        // Assert
        msg.Content.ShouldStartWith("Flag removed!");
        msg.Content.ShouldNotBeNull().ShouldContain("keepFlag");
        msg.Content.ShouldNotBeNull().ShouldNotContain("removeFlag");
    }

    [Test]
    public async Task ShouldRemoveNonExistentFlag()
    {
        // Arrange
        await ChannelFlagBuilder
            .WithChannelId(123)
            .WithFlags(["keepFlag"])
            .GetAsync();
        
        // Send
        var msg = await Mediator.Send(new RemoveFlagRequest<InteractionMessageProperties>
        {
            ChannelId = 123,
            FlagName = "nonExistentFlag"
        });
        
        // Assert
        msg.Content.ShouldStartWith("Channel didn't have flag \"nonExistentFlag\".");
    }
    
    [Test]
    public async Task ShouldListFlags()
    {
        // Arrange
        await ChannelFlagBuilder
            .WithChannelId(123)
            .WithFlags(["flag1", "flag2"])
            .GetAsync();

        // Send
        var msg = await Mediator.Send(new ListFlagsRequest<InteractionMessageProperties>
        {
            ChannelId = 123
        });

        // Assert
        msg.Content.ShouldStartWith("Channel has the following flags:");
        msg.Content.ShouldNotBeNull().ShouldContain("flag1");
        msg.Content.ShouldNotBeNull().ShouldContain("flag2");
    }
}
