using System.Net;
using Dotto.Application.InternalServices.UploadService;
using Minio;
using Minio.DataModel.Args;

namespace Dotto.Infrastructure.FileUpload;

public class MinioUploadService(IMinioClient minioClient, MinioSettings minioSettings) : IUploadService
{
    public async Task<Uri> UploadFile(Stream stream, long fileSize, string? filename, string? contentType, CancellationToken token)
    {
        filename ??= Guid.NewGuid().ToString("N");
        
        var resp = await minioClient.PutObjectAsync(new PutObjectArgs()
            .WithBucket(minioSettings.BucketName)
            .WithObject(filename)
            .WithContentType(contentType)
            .WithObjectSize(fileSize)
            .WithStreamData(stream), token);
        
        var publicLink = new Uri(
            new Uri(minioClient.Config.Endpoint, UriKind.Absolute),
            $"{minioSettings.BucketName}/{WebUtility.UrlEncode(resp.ObjectName)}");
        
        return publicLink;
    }

    public async Task InitializeBucket()
    {
        var mbArgs = new MakeBucketArgs()
            .WithBucket(minioSettings.BucketName)
            .WithHeaders(new Dictionary<string, string>
            {
                { "key", "value" }
            });

        try
        {
            await minioClient.MakeBucketAsync(mbArgs);
        }
        catch (ArgumentException ex)
        {
            // weep about it
            if (!ex.Message.StartsWith("Bucket already owned by you"))
            {
                throw;
            }
        }

        var policy = new SetPolicyArgs()
            .WithBucket(minioSettings.BucketName)
            .WithPolicy(
            $$"""
            {
                "Version": "2012-10-17",
                "Statement": [
                    {
                        "Effect": "Allow",
                        "Principal": {
                            "AWS": [
                                "*"
                            ]
                        },
                        "Action": [
                            "s3:GetObject"
                        ],
                        "Resource": [
                            "arn:aws:s3:::{{minioSettings.BucketName}}/*"
                        ]
                    }
                ]
            }
            """);

        await minioClient.SetPolicyAsync(policy);
    }
}