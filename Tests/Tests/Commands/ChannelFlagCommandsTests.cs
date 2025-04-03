using Dotto.Application.Modules.ChannelFlags;
using MediatR;
using NetCord;
using NetCord.Rest;
using Shouldly;

namespace Tests.Tests.Commands;

public class ChannelFlagCommandsTests : TestFixtureBase
{
    [Test]
    public async Task ShouldAddFlag()
    {
        // Arrange
        var flag = await ChannelFlagBuilder
            .WithChannelId(123)
            .WithFlags(["preflag"])
            .GetAsync();
        
        // Send
        var msg = await Mediator.Send(new AddFlagRequest<InteractionMessageProperties>()
        {
            ChannelId = 123,
            FlagName = "newflag"
        });
        
        // Assert
        msg.Content.ShouldStartWith("Flag added!");
        msg.Content!.ShouldContain("preflag");
        msg.Content!.ShouldContain("newflag");
    }
    
    [Test]
    public async Task ShouldRemoveFlag()
    {
        // Arrange
        var flag = await ChannelFlagBuilder
            .WithChannelId(123)
            .WithFlags(["keepFlag", "removeFlag"])
            .GetAsync();
        
        // Send
        var msg = await Mediator.Send(new RemoveFlagRequest<InteractionMessageProperties>()
        {
            ChannelId = 123,
            FlagName = "removeFlag"
        });
        
        // Assert
        msg.Content.ShouldStartWith("Flag removed!");
        msg.Content!.ShouldContain("keepFlag");
        msg.Content!.ShouldNotContain("removeFlag");
    }
}