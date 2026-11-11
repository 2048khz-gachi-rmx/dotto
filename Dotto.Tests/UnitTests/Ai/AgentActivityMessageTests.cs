using System.Collections.Concurrent;
using Dotto.Ai.Agents.Base;
using Dotto.Discord.Services;
using NetCord.Rest;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Pins the debounce/dispose contract of the live activity renderer using fake
/// update delegates, so no Discord client is involved.
/// </summary>
public class AgentActivityMessageTests
{
    private static (AgentActivityMessage Renderer, Func<IReadOnlyList<string>> Updates, Action Signal)
        Build(AgentProgress progress, TimeSpan debounce, TaskCompletionSource? firstUpdate = null)
    {
        var updates = new ConcurrentQueue<string>();

        Task Update(Action<MessageOptions> apply)
        {
            updates.Enqueue("edit");
            firstUpdate?.TrySetResult();
            return Task.CompletedTask;
        }

        var renderer = new AgentActivityMessage(progress, Update, debounce: debounce);

        return (renderer, () => updates.ToArray(), () => progress.StartCall("ExecuteCommand", "cmd"));
    }

    [Test]
    public async Task SeveralSignalsWithinOneWindow_ProduceOneEdit()
    {
        var progress = new AgentProgress(20);
        var firstUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (renderer, updates, signal) = Build(progress, TimeSpan.FromMilliseconds(60), firstUpdate);

        await using (renderer)
        {
            for (var i = 0; i < 5; i++)
                signal();

            await firstUpdate.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200);

            updates().Count.ShouldBe(1);
            renderer.LastContent.ShouldNotBeNull();
            renderer.LastContent.ShouldContain("ExecuteCommand: cmd");
        }
    }

    [Test]
    public async Task Dispose_DropsAPendingEdit()
    {
        var progress = new AgentProgress(20);
        var (renderer, updates, signal) = Build(progress, TimeSpan.FromMilliseconds(300));

        signal();
        await renderer.DisposeAsync();
        await Task.Delay(500);

        updates().ShouldBeEmpty();
    }

    [Test]
    public async Task Dispose_StopsFurtherEdits()
    {
        var progress = new AgentProgress(20);
        var firstUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (renderer, updates, signal) = Build(progress, TimeSpan.FromMilliseconds(60), firstUpdate);

        signal();
        await firstUpdate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await renderer.DisposeAsync();

        var editCount = updates().Count;
        progress.StartCall("ExecuteCommand", "after dispose");
        await Task.Delay(200);

        updates().Count.ShouldBe(editCount);
    }
}
