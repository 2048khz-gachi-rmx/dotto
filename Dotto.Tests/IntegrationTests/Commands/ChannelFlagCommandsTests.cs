using Dotto.Application.InternalServices;
using Dotto.Common.Constants;
using Dotto.Discord.CommandHandlers.Flags;
using Microsoft.Extensions.Caching.Hybrid;
using NetCord.Rest;
using Shouldly;

namespace Dotto.Tests.IntegrationTests.Commands;

public class ChannelFlagCommandsTests : TestDatabaseFixtureBase
{
    // https://github.com/dotnet/extensions/issues/5763#issuecomment-2688667419
    // we're not testing the cache here; this is a shim so we can actually get the factory's outputs
    private sealed class FakeHybridCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => factory(state, cancellationToken);

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => default;
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => default;
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => default;
    }
    
    private readonly HybridCache _mockCache = new FakeHybridCache();

    private FlagCommandHandler Sut => new(
        new ChannelFlagsService(DbContext, TestDateTimeProvider, _mockCache));

    [Test]
    public async Task ShouldAddFlag()
    {
        // Arrange
        await ChannelFlagBuilder
            .WithChannelId(123)
            .WithFlags(["preflag"])
            .GetAsync();

        // Send
        var msg = await Sut.AddFlag<InteractionMessageProperties>(123, "newflag");

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

        NewScope();

        // Send
        var shouldThrow = async () => await Sut.AddFlag<InteractionMessageProperties>(123, "newflag");

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
        var msg = await Sut.RemoveFlag<InteractionMessageProperties>(123, "removeFlag");

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
        var msg = await Sut.RemoveFlag<InteractionMessageProperties>(123, "nonExistentFlag");

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
        var msg = await Sut.ListFlags<InteractionMessageProperties>(123);

        // Assert
        msg.Content.ShouldStartWith("Channel has the following flags:");
        msg.Content.ShouldNotBeNull().ShouldContain("flag1");
        msg.Content.ShouldNotBeNull().ShouldContain("flag2");
    }
}