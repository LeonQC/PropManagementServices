using Microsoft.Extensions.DependencyInjection;
using SearchService.DataAccess;

namespace SearchService.Business;

/// <summary>
/// Re-exports index provisioning so the Api can create the index + alias through the Business
/// layer (its only project reference) without depending on DataAccess or the OpenSearch client
/// — the same shim pattern the other services use for database initialization.
/// </summary>
public static class IndexStartup
{
    public static async Task InitializeSearchIndexAsync(
        this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPropertyIndex>().EnsureCreatedAsync(ct);
        await scope.ServiceProvider.GetRequiredService<IDealIndex>().EnsureCreatedAsync(ct);
    }
}
