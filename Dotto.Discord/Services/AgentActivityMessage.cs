using System.Threading.Channels;
using Dotto.Ai.Agents.Base;
using Microsoft.Extensions.Logging;
using NetCord.Rest;

namespace Dotto.Discord.Services;

/// <summary>
/// Renders a run's live activity into its own message while the agent works.
/// Signals from <see cref="AgentProgress.Changed"/> are coalesced: one edit per
/// debounce window, showing only the newest calls. The update itself is a delegate,
/// so this has no Discord client dependency and is unit-testable.
/// </summary>
public sealed class AgentActivityMessage : IAsyncDisposable
{
    internal static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(1300);

    private readonly AgentProgress _progress;
    private readonly Func<Action<MessageOptions>, Task> _update;
    private readonly ILogger? _logger;
    private readonly TimeSpan _debounce;
    private readonly int _maxLines;

    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;
    private int _disposed;
    private string? _lastContent;

    /// <summary>
    /// Content handed to the most recent update (exposed for tests; the update
    /// delegate needs a live Discord message to apply it to).
    /// </summary>
    public string? LastContent => _lastContent;

    internal AgentActivityMessage(
        AgentProgress progress,
        Func<Action<MessageOptions>, Task> update,
        ILogger? logger = null,
        TimeSpan? debounce = null,
        int maxLines = AgentProgress.ActivityLines)
    {
        _progress = progress;
        _update = update;
        _logger = logger;
        _debounce = debounce ?? DefaultDebounce;
        _maxLines = Math.Max(1, maxLines);

        _progress.Changed += OnChanged;
        _pump = PumpAsync();
    }

    public static AgentActivityMessage Create(
        AgentProgress progress,
        Func<Action<MessageOptions>, Task> update,
        ILogger? logger = null,
        TimeSpan? debounce = null,
        int maxLines = AgentProgress.ActivityLines) =>
        new(progress, update, logger, debounce, maxLines);

    private void OnChanged()
    {
        if (Volatile.Read(ref _disposed) == 0)
            _signals.Writer.TryWrite(true);
    }

    private async Task PumpAsync()
    {
        try
        {
            while (await _signals.Reader.WaitToReadAsync(_cts.Token))
            {
                // Coalesce bursts that land inside the same window.
                await Task.Delay(_debounce, _cts.Token);
                while (_signals.Reader.TryRead(out _))
                {
                }

                if (_cts.IsCancellationRequested)
                    break;

                var content = AgentProgress.RenderActivity(_progress.Calls, _maxLines);
                if (content.Length == 0 || content == _lastContent)
                    continue;

                _lastContent = content;

                try
                {
                    await _update(options => options.Content = content);
                }
                catch (Exception ex) when (ex is RestException or HttpRequestException)
                {
                    // Best effort: the next edit repairs the display.
                    _logger?.LogDebug(ex, "Could not update the AI activity message");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }

    /// <summary>
    /// Stops pending edits and waits for any in-flight edit so the caller can safely
    /// replace the message with the final answer.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _progress.Changed -= OnChanged;
        _signals.Writer.TryComplete();
        _cts.Cancel();

        try
        {
            await _pump;
        }
        catch (Exception ex)
        {
            // Rendering is cosmetic; never let it take the answer down.
            _logger?.LogDebug(ex, "AI activity renderer stopped with an exception");
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
