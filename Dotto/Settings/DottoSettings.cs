using Dotto.Infrastructure.FileUpload;

namespace Dotto.Settings;

public class DottoSettings
{
    public string ConnectionString { get; init; } = null!;
    public MinioSettings? Minio { get; init; }
}