using System.Collections.Immutable;
using Dotto.Application.InternalServices;
using Dotto.Common.Constants;
using Dotto.Discord.CommandHandlers.Flags;
using NetCord.Rest;
using NSubstitute;
using Shouldly;

namespace Dotto.Tests.UnitTests.CommandHandlers;

public class FlagCommandHandlerTests
{
    [Test]
    public async Task AddFlag_RejectsTooLongFlag()
    {
        // Arrange
        var flagsService = Substitute.For<IChannelFlagsService>();
        var handler = new FlagCommandHandler(flagsService);
        var longFlag = new string('a', Constants.ChannelFlags.MaxLength + 1);

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(
            () => handler.AddFlag<InteractionMessageProperties>(123, longFlag));
    }

    [Test]
    public async Task AddFlag_RejectsInvalidCharacters()
    {
        // Arrange
        var flagsService = Substitute.For<IChannelFlagsService>();
        var handler = new FlagCommandHandler(flagsService);

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(
            () => handler.AddFlag<InteractionMessageProperties>(123, "UPPERCASE"));

        await Should.ThrowAsync<ArgumentException>(
            () => handler.AddFlag<InteractionMessageProperties>(123, "flag-with-dash"));

        await Should.ThrowAsync<ArgumentException>(
            () => handler.AddFlag<InteractionMessageProperties>(123, "flag with space"));
    }

    [Test]
    public async Task AddFlag_AcceptsValidFlag()
    {
        // Arrange
        var flagsService = Substitute.For<IChannelFlagsService>();
        flagsService.AddChannelFlag(123UL, "valid_flag", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        flagsService.GetChannelFlags(123UL, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(ImmutableList.Create("preflag", "valid_flag")));

        var handler = new FlagCommandHandler(flagsService);

        // Act
        var msg = await handler.AddFlag<InteractionMessageProperties>(123, "valid_flag");

        // Assert
        msg.Content.ShouldStartWith("Flag added!");
        msg.Content.ShouldContain("preflag");
        msg.Content.ShouldContain("valid_flag");
    }

    [Test]
    public async Task RemoveFlag_ReturnsSuccessWhenFlagExists()
    {
        // Arrange
        var flagsService = Substitute.For<IChannelFlagsService>();
        flagsService.RemoveChannelFlag(123UL, "remove_flag", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        flagsService.GetChannelFlags(123UL, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(ImmutableList.Create("keep_flag")));

        var handler = new FlagCommandHandler(flagsService);

        // Act
        var msg = await handler.RemoveFlag<InteractionMessageProperties>(123, "remove_flag");

        // Assert
        msg.Content.ShouldStartWith("Flag removed!");
        msg.Content.ShouldContain("keep_flag");
        msg.Content.ShouldNotContain("remove_flag");
    }

    [Test]
    public async Task RemoveFlag_ReturnsNotFoundWhenFlagMissing()
    {
        // Arrange
        var flagsService = Substitute.For<IChannelFlagsService>();
        flagsService.RemoveChannelFlag(123UL, "missing_flag", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var handler = new FlagCommandHandler(flagsService);

        // Act
        var msg = await handler.RemoveFlag<InteractionMessageProperties>(123, "missing_flag");

        // Assert
        msg.Content.ShouldStartWith("Channel didn't have flag \"missing_flag\".");
    }

    [Test]
    public async Task ListFlags_ReturnsFlagsWhenPresent()
    {
        // Arrange
        var flagsService = Substitute.For<IChannelFlagsService>();
        flagsService.GetChannelFlags(123UL, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(ImmutableList.Create("flag1", "flag2")));

        var handler = new FlagCommandHandler(flagsService);

        // Act
        var msg = await handler.ListFlags<InteractionMessageProperties>(123);

        // Assert
        msg.Content.ShouldStartWith("Channel has the following flags:");
        msg.Content.ShouldContain("flag1");
        msg.Content.ShouldContain("flag2");
    }

    [Test]
    public async Task ListFlags_ReturnsEmptyMessageWhenNoFlags()
    {
        // Arrange
        var flagsService = Substitute.For<IChannelFlagsService>();
        flagsService.GetChannelFlags(123UL, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(ImmutableList<string>.Empty));

        var handler = new FlagCommandHandler(flagsService);

        // Act
        var msg = await handler.ListFlags<InteractionMessageProperties>(123);

        // Assert
        msg.Content.ShouldStartWith("Channel has no flags.");
    }
}
