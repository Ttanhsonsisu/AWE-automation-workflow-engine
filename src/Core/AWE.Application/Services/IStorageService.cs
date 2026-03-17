namespace AWE.Application.Services;

/// <summary>
/// Abstraction for object storage (MinIO / AWS S3)
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Uploads an object to storage.
    /// </summary>
    Task PutObjectAsync(
        string bucket,
        string objectKey,
        Stream content,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an object from storage and returns its content as a stream.
    /// Caller is responsible for disposing the returned stream.
    /// </summary>
    Task<Stream> GetObjectAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an object exists in storage.
    /// </summary>
    Task<bool> ExistsAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an object from storage (no-op if not found).
    /// </summary>
    Task DeleteObjectAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures bucket exists. Creates it if missing.
    /// </summary>
    Task EnsureBucketExistsAsync(
        string bucket,
        CancellationToken cancellationToken = default);
}
