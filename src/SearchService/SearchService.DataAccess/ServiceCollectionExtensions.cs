using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenSearch.Net;

namespace SearchService.DataAccess;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the search store. Mirrors the other services' AddDataAccess, except the store
    /// is OpenSearch rather than Postgres — so there's no DbContext, no migrations, and no
    /// connection string.
    /// </summary>
    public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration config)
    {
        var settings = config.GetSection(OpenSearchSettings.SectionName).Get<OpenSearchSettings>()
                       ?? new OpenSearchSettings();
        services.AddSingleton(settings);

        // The low-level client is thread-safe and pools connections — register it once.
        services.AddSingleton<IOpenSearchLowLevelClient>(_ =>
        {
            var connectionSettings = new ConnectionConfiguration(new Uri(settings.Url))
                .RequestTimeout(TimeSpan.FromSeconds(30));
            return new OpenSearchLowLevelClient(connectionSettings);
        });

        services.AddSingleton<IPropertyIndex, OpenSearchPropertyIndex>();

        return services;
    }
}
