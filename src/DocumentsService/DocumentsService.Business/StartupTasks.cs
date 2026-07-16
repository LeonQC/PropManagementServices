using DocumentsService.Business.Storage;
using DocumentsService.DataAccess;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentsService.Business;

/// <summary>
/// Re-exports startup provisioning so the Api can trigger it through the Business
/// layer (its only project reference) without a direct dependency on DataAccess,
/// EF Core, or the S3 SDK: apply migrations, then make sure the bucket exists.
/// </summary>
public static class StartupTasks
{
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
        => await DatabaseInitializer.InitializeAsync(services, ct);

    public static async Task EnsureStorageAsync(this IServiceProvider services, CancellationToken ct = default)
        => await services.GetRequiredService<IBlobStorage>().EnsureBucketAsync(ct);
}
