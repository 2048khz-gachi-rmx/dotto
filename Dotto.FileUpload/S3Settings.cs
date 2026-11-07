using System.ComponentModel.DataAnnotations;

namespace Dotto.Infrastructure.FileUpload;

public record S3Settings
{
    [Required(ErrorMessage = "S3 Access key is missing")]
    public string AccessKey { get; init; } = null!;
    
    [Required(ErrorMessage = "S3 Secret key is missing")]
    public string SecretKey { get; init; } = null!;
    
    [Required(ErrorMessage = "S3 Bucket name is missing")]
    public string BucketName { get; init; } = null!;
    
    public string Region { get; init; } = null!;
    
    public Uri? BaseUrl { get; init; } = null!;

    /// <summary>Largest file the bot will send to S3, in bytes. Defaults to 100 MiB.</summary>
    [Range(1, long.MaxValue, ErrorMessage = "S3 max file size must be positive")]
    public long MaxFileSizeBytes { get; init; } = 100 << 20;
}