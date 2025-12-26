using Amazon.Runtime;
using Amazon.S3;
using Dotto.Application.Abstractions.Upload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Dotto.Infrastructure.FileUpload;

public static class DependencyInjection
{
    public static IServiceCollection AddFileUploader(this IServiceCollection services)
    {
        services.AddOptions<S3Settings>()
            .BindConfiguration("Minio") // womp
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        // build a service provider just for resolving the s3 settings. feels like a giant hack
        // but the alternative is accepting the IConfiguration from the caller, which is even more meh
        using var serviceProvider = services.BuildServiceProvider();
        var s3Settings = serviceProvider.GetRequiredService<IOptions<S3Settings>>().Value;

        // if S3 base URL isn't set, don't register anything
        if (s3Settings.BaseUrl == null)
            return services;

        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<S3Settings>>().Value;

            return new AmazonS3Client(
                new BasicAWSCredentials(s3Settings.AccessKey, s3Settings.SecretKey),
                new AmazonS3Config
                {
                    ServiceURL = "https://s3.badcoder.dev",
                    ForcePathStyle = true,
                    LogMetrics = false,
                    AuthenticationRegion = options.Region
                });
        });
        
        services.AddTransient<IUploadService, S3UploadService>();
        services.AddTransient<S3UploadService>();

        return services;
    }

    public static async Task InitializeS3Uploader(this IHost host)
    {
        var uploadService = host.Services.GetService<S3UploadService>();
        
        if (uploadService == null)
            return;
        
        await uploadService.InitializeBucket();
    }
}