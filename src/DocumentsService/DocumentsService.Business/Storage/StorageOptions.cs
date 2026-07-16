namespace DocumentsService.Business.Storage;

/// <summary>
/// Blob storage settings, bound from the "Storage" config section. Locally this
/// points at MinIO; in prod the same shape points at AWS S3 (config-only swap).
/// </summary>
public class StorageOptions
{
    /// <summary>Endpoint the SERVICE uses for server-side ops (in-network, e.g. http://minio:9000).</summary>
    public string ServiceUrl { get; set; } = "http://localhost:9000";

    /// <summary>
    /// Endpoint the BROWSER can reach. Presigned URLs must be generated against
    /// this host — the host is part of the SigV4 signature, so it can't be
    /// rewritten after signing.
    /// </summary>
    public string PublicUrl { get; set; } = "http://localhost:9000";

    public string Bucket { get; set; } = "proptrack-documents";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    public string Region { get; set; } = "us-east-1";

    /// <summary>Presigned URL lifetime (spec: 15 minutes).</summary>
    public int PresignExpiryMinutes { get; set; } = 15;
}
