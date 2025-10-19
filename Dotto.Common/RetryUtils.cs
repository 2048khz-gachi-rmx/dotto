namespace Dotto.Common;

public static class RetryUtils
{
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        Func<Exception, bool>? shouldRetry = null,
        int maxRetries = 2,
        int initialDelayMs = 250,
        CancellationToken cancellationToken = default)
    {
        shouldRetry ??= _ => true;
        var retryCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (
                // don't retry if the exception was due to our cancellation token being cancelled
                !(ex is TaskCanceledException tce && tce.CancellationToken == cancellationToken)
                && shouldRetry(ex)
                && retryCount < maxRetries)
            {
                retryCount++;
                var exponentialDelay = (int)(initialDelayMs * Math.Pow(2, retryCount - 1));
                var jitterAmount = (int)(Random.Shared.NextDouble() * 0.5 * exponentialDelay);
                var totalDelay = exponentialDelay + jitterAmount;

                try
                {
                    await Task.Delay(totalDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }
            }
        }
    }

    public static async Task ExecuteWithRetryAsync(
        Func<Task> action,
        Func<Exception, bool>? shouldRetry = null,
        int maxRetries = 3,
        int initialDelayMs = 200,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(
            async () => 
            {
                await action().ConfigureAwait(false);
                return true;
            },
            shouldRetry,
            maxRetries,
            initialDelayMs,
            cancellationToken).ConfigureAwait(false);
    }
}