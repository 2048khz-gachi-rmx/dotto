namespace Dotto.Common.DateTimeProvider;

public interface IDateTimeProvider
{
    public DateTime UtcNow { get; }
}