using Dotto.Application.Abstractions.Upload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minio;

namespace Dotto.Infrastructure.FileUpload;

public static class DependencyInjection
{
    public static IServiceCollection AddFileUploader(this IServiceCollection services)
    {
        services.AddOptions<MinioSettings>()
            .BindConfiguration("Minio")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        // build a service provider just for resolving the minio settings. feels like a giant hack
        // but the alternative is accepting the IConfiguration from the caller, which is even more meh
        using var serviceProvider = services.BuildServiceProvider();
        var minioSettings = serviceProvider.GetRequiredService<IOptions<MinioSettings>>().Value;

        // if MinIO base URL isn't set, don't register anything
        if (minioSettings.BaseUrl == default)
            return services;
        
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MinioSettings>>().Value;
                
            return new MinioClient()
                .WithEndpoint(options.BaseUrl)
                .WithRegion(options.Region)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .Build();
        });
        
        services.AddTransient<IUploadService, MinioUploadService>();
        services.AddTransient<MinioUploadService>();

        return services;
    }

    public static async Task InitializeMinioUploader(this IHost host)
    {
        var uploadService = host.Services.GetService<MinioUploadService>();
        
        if (uploadService == null)
            return;
        
        await uploadService.InitializeBucket();
    }
}