using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Dotto.Application.Abstractions.Upload;
using Microsoft.Extensions.Options;

namespace Dotto.Infrastructure.FileUpload;

public class S3UploadService(IAmazonS3 s3Client, IOptions<S3Settings> s3Settings) : IUploadService
{
    private S3Settings S3Settings => s3Settings.Value;
    
    public async Task<Uri> UploadFile(Stream stream, long fileSize, string? filename, string? contentType, CancellationToken token)
    {
        filename ??= Guid.NewGuid().ToString("N");

        var request = new PutObjectRequest()
        {
            BucketName = s3Settings.Value.BucketName,
            Key = filename,
            ContentType = contentType,
            AutoCloseStream = false,
            InputStream = stream
        };
        
        await s3Client.PutObjectAsync(request, token);
        
        var publicLink = new Uri(
            new Uri(s3Client.Config.ServiceURL, UriKind.Absolute),
            $"{S3Settings.BucketName}/{WebUtility.UrlEncode(filename)}");
        
        return publicLink;
    }

    public async Task InitializeBucket()
    {
        var mbArgs = new PutBucketRequest()
        {
            BucketName = S3Settings.BucketName
        };

        try
        {
            await s3Client.PutBucketAsync(mbArgs);
        }
        catch (BucketAlreadyOwnedByYouException ex) { /* weep about it */ }

        var policy = new PutBucketPolicyRequest()
        {
            BucketName = S3Settings.BucketName,
            Policy = 
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
                                  "arn:aws:s3:::{{S3Settings.BucketName}}/*"
                              ]
                          }
                      ]
                  }
                  """
        };

        await s3Client.PutBucketPolicyAsync(policy);
    }
}