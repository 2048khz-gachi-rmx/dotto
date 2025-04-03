using Dotto.Application.InternalServices.UploadService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minio;

namespace Dotto.Infrastructure.FileUpload;

public static class DependencyInjection
{
    public static IServiceCollection AddFileUploader(this IServiceCollection services, MinioSettings? settings)
    {
        if (settings?.BaseUrl != null)
        {
            ArgumentNullException.ThrowIfNull(settings.AccessKey, "Settings.Minio.AccessKey");
            ArgumentNullException.ThrowIfNull(settings.SecretKey, "Settings.Minio.SecretKey");
            ArgumentNullException.ThrowIfNull(settings.BucketName, "Settings.Minio.BucketName");

            services.AddMinio(cfg => cfg
                .WithEndpoint(settings.BaseUrl)
                .WithRegion(settings.Region)
                .WithCredentials(settings.AccessKey, settings.SecretKey));

            services.AddSingleton(settings);
            services.AddTransient<IUploadService, MinioUploadService>();
            services.AddTransient<MinioUploadService>();
        }
        
        return services;
    }

    public static async Task InitializeMinioUploader(this IHost host)
    {
        await host.Services.GetRequiredService<MinioUploadService>()
            .InitializeBucket();
    }
}