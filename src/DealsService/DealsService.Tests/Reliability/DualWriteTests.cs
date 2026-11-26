using DealsService.Business;
using DealsService.Business.DTOs;
using DealsService.Business.Events;
using DealsService.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PropTrack.Messaging;
using Xunit;

namespace DealsService.Tests.Reliability;

/// <summary>
/// The dual write: DealService commits to Postgres and then publishes to Kafka as a separate
/// step. Slice 0 of docs/kafka-reliability-plan.md — this test states the guarantee the
/// system should give and fails until the outbox (slice 3) provides it. Skipped while that
/// work is paused.
/// </summary>
[Collection(nameof(KafkaPostgresCollection))]
[Trait("Category", "Integration")]
public class DualWriteTests(KafkaPostgresFixture fixture)
{
    [Fact(Skip = "Known gap: the dual write loses events until the outbox (slice 3 of " +
                 "docs/kafka-reliability-plan.md) is built. Remove this Skip to see it fail.")]
    public async Task A_committed_deal_still_gets_its_created_event_when_the_publish_fails()
    {
        fixture.Require();

        var propertyId = $"prop-{Guid.NewGuid():N}";
        using var kafka = new KafkaEventPublisher(fixture.Kafka, NullLogger<KafkaEventPublisher>.Instance);
        var publisher = new FailsOncePublisher(kafka);

        await using (var db = fixture.NewDbContext())
        {
            var repo = new DealRepository(db);
            var service = new DealService(repo, publisher, new DealSnapshotPublisher(repo, publisher));

            // The broker "goes away" for exactly the publish that follows the commit. Whether
            // the caller sees an exception is not the point; what the system ends up holding is.
            try
            {
                await service.CreateAsync(NewDeal(propertyId), actorId: "user-1");
            }
            catch (BrokerDownException)
            {
            }
        }

        Assert.True(publisher.Failed, "The test never injected its publish failure.");

        await using (var db = fixture.NewDbContext())
            Assert.True(await db.Deals.AnyAsync(d => d.PropertyId == propertyId),
                "Precondition: the deal should have been committed before the publish failed.");

        // The broker is healthy again from here on. Anything that was going to deliver the
        // event has ten seconds to do it.
        var events = fixture.ReadByKey(Topics.DealCreated, propertyId, TimeSpan.FromSeconds(10));

        Assert.True(events.Count > 0,
            $"Deal for {propertyId} is committed in Postgres but no {Topics.DealCreated} event " +
            "ever reached Kafka: the event was lost between the database commit and the publish.");
    }

    private static CreateDealDto NewDeal(string propertyId) => new(
        propertyId, "Metro Pkwy Warehouse Complex", "Industrial", "New York Metro",
        OccupancyRate: 0.88, MarketCapRateBenchmark: 0.065, Name: null, Priority: null,
        OfferPrice: 12_500_000, ProjectedCapRate: 0.07, TargetIrr: 0.14, EquityMultiple: 1.85,
        ProjectedCloseDate: null);

    private sealed class BrokerDownException() : Exception("Simulated broker outage.");

    /// <summary>Fails the first publish, then delegates to the real broker.</summary>
    private sealed class FailsOncePublisher(IEventPublisher inner) : IEventPublisher
    {
        public bool Failed { get; private set; }

        public Task PublishAsync<T>(string topic, string key, T payload, CancellationToken ct = default)
        {
            if (Failed) return inner.PublishAsync(topic, key, payload, ct);
            Failed = true;
            throw new BrokerDownException();
        }
    }
}
