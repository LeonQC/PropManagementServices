using DealsService.Business.Events;
using Microsoft.Extensions.DependencyInjection;
using PropTrack.Messaging;

namespace DealsService.Business;

/// <summary>
/// Declares the Kafka topics this service owns that need non-default configuration, so the
/// Api can trigger it through the Business layer (its only project reference) without
/// depending on the messaging library directly — same shape as <see cref="DatabaseStartup"/>.
/// </summary>
public static class MessagingStartup
{
    /// <summary>
    /// Ensures deal.snapshot exists as a compacted topic before anything publishes to it.
    /// The broker has auto-create enabled, so skipping this would silently yield the default
    /// delete policy and break the "replay the topic to rebuild the index" guarantee.
    /// </summary>
    public static async Task EnsureMessagingTopicsAsync(
        this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<TopicProvisioner>();
        await provisioner.EnsureCompactedTopicAsync(Topics.DealSnapshot, ct: ct);
    }
}
