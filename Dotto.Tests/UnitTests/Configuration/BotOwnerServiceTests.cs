using Dotto.Application.Settings;
using Dotto.Discord.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Dotto.Tests.UnitTests.Configuration;

public class BotOwnerServiceTests
{
    private static BotOwnerService Build(
        ulong[] configuredOwners,
        IApplicationOwnerProvider ownerProvider)
        => new(
            Options.Create(new BotSettings { OwnerIds = configuredOwners }),
            ownerProvider,
            NullLogger<BotOwnerService>.Instance);

    [Test]
    public async Task ListedOwner_IsAuthorized_WithoutCallingDiscord()
    {
        var provider = Substitute.For<IApplicationOwnerProvider>();
        var service = Build([10, 20], provider);

        (await service.IsOwnerAsync(20)).ShouldBeTrue();

        await provider.DidNotReceiveWithAnyArgs().GetOwnerIdAsync(default);
    }

    [Test]
    public async Task UnlistedUser_IsRejected_WhenOwnersAreConfigured()
    {
        var provider = Substitute.For<IApplicationOwnerProvider>();
        var service = Build([10, 20], provider);

        (await service.IsOwnerAsync(30)).ShouldBeFalse();
    }

    [Test]
    public async Task EmptyOwnerList_FallsBackToApplicationOwner()
    {
        var provider = Substitute.For<IApplicationOwnerProvider>();
        provider.GetOwnerIdAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<ulong?>(42));

        var service = Build([], provider);

        (await service.IsOwnerAsync(42)).ShouldBeTrue();
        (await service.IsOwnerAsync(43)).ShouldBeFalse();
    }

    [Test]
    public async Task ApplicationOwner_IsResolvedOnlyOnce()
    {
        var provider = Substitute.For<IApplicationOwnerProvider>();
        provider.GetOwnerIdAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<ulong?>(42));

        var service = Build([], provider);

        await service.IsOwnerAsync(42);
        await service.IsOwnerAsync(42);

        await provider.Received(1).GetOwnerIdAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TeamOwnedApplication_WithoutConfiguredOwners_AuthorizesNoOne()
    {
        var provider = Substitute.For<IApplicationOwnerProvider>();
        provider.GetOwnerIdAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<ulong?>(null));

        var service = Build([], provider);

        (await service.IsOwnerAsync(42)).ShouldBeFalse();
    }

    [Test]
    public async Task ApiFailure_DeniesAccessInsteadOfThrowing()
    {
        var provider = Substitute.For<IApplicationOwnerProvider>();
        provider.GetOwnerIdAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ulong?>(new HttpRequestException("discord down")));

        var service = Build([], provider);

        (await service.IsOwnerAsync(42)).ShouldBeFalse();
    }

    [Test]
    public async Task ApiFailure_IsRetriedOnTheNextCheck()
    {
        var provider = Substitute.For<IApplicationOwnerProvider>();
        provider.GetOwnerIdAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<ulong?>(new HttpRequestException("discord down")),
                Task.FromResult<ulong?>(42));

        var service = Build([], provider);

        (await service.IsOwnerAsync(42)).ShouldBeFalse();
        (await service.IsOwnerAsync(42)).ShouldBeTrue();
    }
}
