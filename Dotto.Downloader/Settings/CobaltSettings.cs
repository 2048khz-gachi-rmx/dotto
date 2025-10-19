namespace Dotto.Infrastructure.Downloader.Settings;

public class CobaltSettings
{
    public required Uri BaseUrl { get; init; }
    
    public required string ApiKey { get; init; }
}