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
        services.AddOptions<MinioSettings>()
            .Bind(settings)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        var minioSettings = settings.Get<MinioSettings>()!;
        
        if (minioSettings.BaseUrl != default)
        {
            services.AddSingleton(s => s.GetRequiredService<IOptions<MinioSettings>>().Value);
            
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