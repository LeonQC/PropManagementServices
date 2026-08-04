using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PropTrack.Messaging;
using SearchService.Business.Consumers;
using SearchService.DataAccess;

namespace SearchService.Business;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusiness(this IServiceCollection services, IConfiguration config)
    {
        services.AddDataAccess(config);
        services.AddKafkaMessaging(config);

        services.AddScoped<PropertyIndexingService>();

        // Inbound event consumers (background services).
        services.AddHostedService<PropertySnapshotConsumer>();

        return services;
    }
}
