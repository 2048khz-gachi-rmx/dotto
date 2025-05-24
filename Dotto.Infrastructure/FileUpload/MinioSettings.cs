using System.ComponentModel.DataAnnotations;

namespace Dotto.Infrastructure.FileUpload;

public record MinioSettings
{
    [Required(ErrorMessage = "MinIO Access key is missing")]
    public string AccessKey { get; init; } = null!;
    
    [Required(ErrorMessage = "MinIO Secret key is missing")]
    public string SecretKey { get; init; } = null!;
    
    [Required(ErrorMessage = "MinIO Bucket name is missing")]
    public string BucketName { get; init; } = null!;
    
    public string Region { get; init; } = null!;
    
    public Uri? BaseUrl { get; init; } = null!;
}