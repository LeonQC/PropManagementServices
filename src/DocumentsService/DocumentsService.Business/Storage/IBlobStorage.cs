namespace DocumentsService.Business.Storage;

/// <summary>
/// S3-compatible blob operations. Bytes never transit this service's API — clients
/// upload/download directly via presigned URLs.
/// </summary>
public interface IBlobStorage
{
    /// <summary>Presigned PUT URL the browser uploads to. Content type is signed in.</summary>
    string GetUploadUrl(string storageKey, string contentType, out DateTime expiresAt);

    /// <summary>Presigned GET URL with a content-disposition filename. 15-min expiry.</summary>
    string GetDownloadUrl(string storageKey, string fileName, out DateTime expiresAt);

    /// <summary>Whether the blob exists (used by confirm to verify the upload happened).</summary>
    Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Creates the bucket if missing (startup; avoids an init sidecar).</summary>
    Task EnsureBucketAsync(CancellationToken ct = default);
}
