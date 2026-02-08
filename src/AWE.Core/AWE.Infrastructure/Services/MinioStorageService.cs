using AWE.Application.Services;
using AWE.Infrastructure.ConfigOptions;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace AWE.Infrastructure.Services;

public class MinioStorageService(IMinioClient _minioClient, IOptions<MinioOptions> _options) : IStorageService
{
    public async Task EnsureBucketExistsAsync(
        string bucket,
        CancellationToken cancellationToken = default)
    {
        var exists = await _minioClient.BucketExistsAsync(
            new BucketExistsArgs()
                .WithBucket(bucket),
            cancellationToken);

        if (!exists)
        {
            await _minioClient.MakeBucketAsync(
                new MakeBucketArgs()
                    .WithBucket(bucket),
                cancellationToken);
        }
    }

    public async Task PutObjectAsync(
        string bucket,
        string objectKey,
        Stream content,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(bucket, cancellationToken);

        content.Position = 0;

        await _minioClient.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithStreamData(content)
                .WithObjectSize(content.Length)
                .WithContentType(contentType),
            cancellationToken);
    }

    public async Task<Stream> GetObjectAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var memoryStream = new MemoryStream();

        await _minioClient.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithCallbackStream(stream =>
                {
                    stream.CopyTo(memoryStream);
                }),
            cancellationToken);

        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task<bool> ExistsAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _minioClient.StatObjectAsync(
                new StatObjectArgs()
                    .WithBucket(bucket)
                    .WithObject(objectKey),
                cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task DeleteObjectAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        await _minioClient.RemoveObjectAsync(
            new RemoveObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey),
            cancellationToken);
    }
}
