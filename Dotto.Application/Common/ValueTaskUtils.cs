namespace Dotto.Common;

public class ValueTaskUtils
{
    public static async ValueTask<T[]> WhenAll<T>(params ValueTask<T>[] tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (tasks.Length == 0)
            return Array.Empty<T>();

        var results = new T[tasks.Length];
        for (var i = 0; i < tasks.Length; i++)
            results[i] = await tasks[i].ConfigureAwait(false);

        return results;
    }
    
    public static async ValueTask WhenAll(params ValueTask[] tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (tasks.Length == 0)
            return;

        for (var i = 0; i < tasks.Length; i++)
            await tasks[i].ConfigureAwait(false);
    }
}