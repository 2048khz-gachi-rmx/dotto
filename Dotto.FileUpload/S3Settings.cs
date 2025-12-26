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
}