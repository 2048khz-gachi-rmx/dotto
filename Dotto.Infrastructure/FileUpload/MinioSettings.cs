namespace Dotto.Infrastructure.FileUpload;

public record MinioSettings
{
    public string AccessKey { get; init; } = null!;
    public string SecretKey { get; init; } = null!;
    public string BucketName { get; init; } = null!;
    public string Region { get; init; } = null!;
    public Uri? BaseUrl { get; init; } = null!;
}