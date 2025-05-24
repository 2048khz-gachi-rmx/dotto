using Dotto.Application.InternalServices.UploadService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minio;

namespace Dotto.Infrastructure.FileUpload;

public static class DependencyInjection
{
    public static IServiceCollection AddFileUploader(this IServiceCollection services, IConfigurationSection settings)
    {
        if (settings.Exists())
        {
            services.AddOptions<MinioSettings>()
                .Bind(settings)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            
            services.AddSingleton(s => s.GetRequiredService<IOptions<MinioSettings>>().Value);

            var minioSettings = settings.Get<MinioSettings>()!;
            
            services.AddMinio(cfg => cfg
                .WithEndpoint(minioSettings.BaseUrl)
                .WithRegion(minioSettings.Region)
                .WithCredentials(minioSettings.AccessKey, minioSettings.SecretKey));
        
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