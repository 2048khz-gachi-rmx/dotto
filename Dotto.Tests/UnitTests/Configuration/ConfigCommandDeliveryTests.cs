using Dotto.Discord.Services;
using Shouldly;

namespace Dotto.Tests.UnitTests.Configuration;

public class ConfigCommandDeliveryTests
{
    [Test]
    public async Task Deliver_SendsTheResponseToDms_WhenAvailable()
    {
        var replies = new List<string>();

        await ConfigCommandDelivery.DeliverAsync(
            () => Task.FromResult(true),
            content =>
            {
                replies.Add(content);
                return Task.CompletedTask;
            },
            "the secret value");

        replies.ShouldBeEmpty();
    }

    [Test]
    public async Task Deliver_FallsBackToAShortInstruction_WhenDmFails()
    {
        var replies = new List<string>();

        await ConfigCommandDelivery.DeliverAsync(
            () => Task.FromResult(false),
            content =>
            {
                replies.Add(content);
                return Task.CompletedTask;
            },
            "the secret value");

        replies.ShouldBe([ConfigCommandDelivery.DirectMessageFallback]);
        replies.Single().ShouldNotContain("the secret value");
    }
}
