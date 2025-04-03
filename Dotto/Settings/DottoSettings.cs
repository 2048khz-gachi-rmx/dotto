using Dotto.Infrastructure.FileUpload;

namespace Dotto.Settings;

public class DottoSettings
{
    public const string SectionName = "Dotto";
    
    public string ConnectionString { get; init; }
    public MinioSettings? Minio { get; init; }
}