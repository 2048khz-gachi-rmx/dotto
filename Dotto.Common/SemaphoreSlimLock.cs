namespace Dotto.Common;

/// <summary>
/// An async-disposable scope that releases a <see cref="SemaphoreSlim"/> on disposal.
/// Acquire via <see cref="SemaphoreSlimExtensions.CaptureWaitAsync"/> so the wait and
/// the release are guaranteed to be balanced, including on exceptions:
/// <code>await using var _ = _gate.CaptureWaitAsync(ct);</code>
/// </summary>
public readonly struct SemaphoreSlimLock(SemaphoreSlim semaphore)
{
    /// <summary>Releases the captured semaphore exactly once.</summary>
    public ValueTask DisposeAsync()
    {
        semaphore.Release();
        return ValueTask.CompletedTask;
    }
}

public static class SemaphoreSlimExtensions
{
    /// <summary>
    /// Awaits the semaphore and returns a scope that releases it on disposal.
    /// If the wait is cancelled (or throws), no lock is held and nothing needs releasing.
    /// </summary>
    public static async ValueTask<SemaphoreSlimLock> CaptureWaitAsync(
        this SemaphoreSlim semaphore,
        CancellationToken cancellationToken = default)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreSlimLock(semaphore);
    }
}
