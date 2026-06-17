using ListingsService.Business.Consumers;
using ListingsService.DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PropTrack.Messaging;

namespace ListingsService.Business;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusiness(this IServiceCollection services, IConfiguration config)
    {
        services.AddDataAccess(config.GetConnectionString("ListingsDb")!);
        services.AddKafkaMessaging(config);

        services.AddScoped<PropertyService>();

        // Inbound event consumers (background services).
        services.AddHostedService<DealCreatedConsumer>();
        services.AddHostedService<DealOutcomeRecordedConsumer>();
        services.AddHostedService<AiPropertySummaryReadyConsumer>();

        return services;
    }
}
