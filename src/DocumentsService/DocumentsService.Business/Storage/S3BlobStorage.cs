using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentsService.Business.Storage;

/// <summary>
/// AWSSDK.S3 implementation working against MinIO locally (prod: real S3, same
/// code). Two clients on purpose: server-side ops use the in-network endpoint,
/// while presigning uses the browser-visible endpoint — the host is part of the
/// SigV4 signature, so presigned URLs must be generated against the host the
/// browser will actually hit.
/// </summary>
public sealed class S3BlobStorage : IBlobStorage, IDisposable
{
    private readonly StorageOptions _options;
    private readonly ILogger<S3BlobStorage> _logger;
    private readonly AmazonS3Client _serviceClient;
    private readonly AmazonS3Client _presignClient;

    public S3BlobStorage(IOptions<StorageOptions> options, ILogger<S3BlobStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
        _serviceClient = CreateClient(_options.ServiceUrl);
        _presignClient = CreateClient(_options.PublicUrl);
    }

    private AmazonS3Client CreateClient(string endpoint) =>
        new(new BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = endpoint,
                // MinIO serves buckets as path segments, not subdomains.
                ForcePathStyle = true,
                AuthenticationRegion = _options.Region,
                // The SDK's default checksum injection adds x-amz-checksum-* headers
                // that S3-compatible stores can reject; only send when required.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });

    public string GetUploadUrl(string storageKey, string contentType, out DateTime expiresAt)
    {
        expiresAt = DateTime.UtcNow.AddMinutes(_options.PresignExpiryMinutes);
        return _presignClient.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = storageKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
            ContentType = contentType,
            Protocol = ProtocolFor(_options.PublicUrl),
        });
    }

    public string GetDownloadUrl(string storageKey, string fileName, out DateTime expiresAt)
    {
        expiresAt = DateTime.UtcNow.AddMinutes(_options.PresignExpiryMinutes);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = storageKey,
            Verb = HttpVerb.GET,
            Expires = expiresAt,
            Protocol = ProtocolFor(_options.PublicUrl),
        };
        request.ResponseHeaderOverrides.ContentDisposition =
            $"attachment; filename=\"{fileName.Replace("\"", "")}\"";
        return _presignClient.GetPreSignedURL(request);
    }

    public async Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        try
        {
            await _serviceClient.GetObjectMetadataAsync(_options.Bucket, storageKey, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task EnsureBucketAsync(CancellationToken ct = default)
    {
        // Retry with backoff: on a fresh compose-up MinIO accepts connections a
        // few seconds after the app starts (same race as Postgres).
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _serviceClient.PutBucketAsync(_options.Bucket, ct);
                _logger.LogInformation("Created bucket {Bucket}.", _options.Bucket);
                return;
            }
            catch (AmazonS3Exception ex) when (
                ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
                _logger.LogInformation("Bucket {Bucket} already exists.", _options.Bucket);
                return;
            }
            catch (Exception ex) when (attempt < 6)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning("Storage not ready (attempt {Attempt}): {Message}. Retrying in {Delay}s.",
                    attempt, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    private static Protocol ProtocolFor(string endpoint) =>
        endpoint.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? Protocol.HTTPS : Protocol.HTTP;

    public void Dispose()
    {
        _serviceClient.Dispose();
        _presignClient.Dispose();
    }
}
