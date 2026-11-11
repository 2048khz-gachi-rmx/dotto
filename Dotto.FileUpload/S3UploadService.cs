using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Dotto.Application.Abstractions.Upload;
using Microsoft.Extensions.Options;

namespace Dotto.Infrastructure.FileUpload;

public class S3UploadService(IAmazonS3 s3Client, IOptions<S3Settings> s3Settings) : IUploadService
{
    private S3Settings S3Settings => s3Settings.Value;

    public long MaxFileSizeBytes => S3Settings.MaxFileSizeBytes;

    public async Task<Uri> UploadFile(Stream stream, long fileSize, string? filename, string? contentType, CancellationToken token)
    {
        // Objects are keyed by a random file id, not the original filename:
        // filenames are attacker-influenced and the bucket is public-read, so a
        // repeat name could overwrite an unrelated object. The original name is
        // preserved as the download filename instead.
        var key = Guid.NewGuid().ToString("N");
        var request = new PutObjectRequest()
        {
            BucketName = s3Settings.Value.BucketName,
            Key = key,
            ContentType = contentType,
            AutoCloseStream = false,
            InputStream = stream
        };

        if (filename is not null)
            request.Headers.ContentDisposition =
                $"attachment; filename*=UTF-8''{Uri.EscapeDataString(filename)}";

        await s3Client.PutObjectAsync(request, token);
        
        var publicLink = new Uri(
            new Uri(s3Client.Config.ServiceURL, UriKind.Absolute),
            $"{S3Settings.BucketName}/{WebUtility.UrlEncode(key)}");
        
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
        catch (BucketAlreadyOwnedByYouException) { /* weep about it */ }

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