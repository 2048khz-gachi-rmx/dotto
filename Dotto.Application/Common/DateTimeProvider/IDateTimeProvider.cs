namespace Dotto.Common.DateTimeProvider;

public interface IDateTimeProvider
{
    public DateTime UtcNow { get; }
    
    public DateOnly UtcToday => DateOnly.FromDateTime(UtcNow);
}