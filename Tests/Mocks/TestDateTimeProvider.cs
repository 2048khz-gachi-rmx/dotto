using Dotto.Common.DateTimeProvider;

namespace Tests.Mocks;

public class TestDateTimeProvider : IDateTimeProvider
{
    private static DateTime? _fixedNow;

    public DateTime UtcNow => _fixedNow ?? DateTime.UtcNow;

    public DateTime SetNow(DateTime? now = null)
    {
        _fixedNow = now ?? DateTime.UtcNow;
        return _fixedNow.Value;
    }

    public void Reset()
    {
        _fixedNow = null;
    }
}