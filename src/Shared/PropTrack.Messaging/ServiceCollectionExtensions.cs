using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PropTrack.Messaging;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared Kafka transport: <see cref="KafkaSettings"/> (from the
    /// "Kafka" config section) and the singleton <see cref="IEventPublisher"/>.
    /// Consumers are registered by the owning service (they are service-specific types).
    /// </summary>
    public static IServiceCollection AddKafkaMessaging(this IServiceCollection services, IConfiguration config)
    {
        var settings = config.GetSection(KafkaSettings.SectionName).Get<KafkaSettings>() ?? new KafkaSettings();
        services.AddSingleton(settings);
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        return services;
    }
}
