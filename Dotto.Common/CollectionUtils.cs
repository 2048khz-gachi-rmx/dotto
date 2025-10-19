using System.Diagnostics.CodeAnalysis;

namespace Dotto.Common;

public static class CollectionUtils
{
    public static bool IsEmpty<T>(this IEnumerable<T> enumerable)
    {
        return !enumerable.Any();
    }
    
    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] this IEnumerable<T>? enumerable)
    {
        return enumerable == null || !enumerable.Any();
    }
}