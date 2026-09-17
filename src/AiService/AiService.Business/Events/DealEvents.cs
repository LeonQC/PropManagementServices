namespace AiService.Business.Events;

/// <summary>
/// The whole projection of a deal, republished by deals-service on every mutation. A local
/// copy of the record owned by <c>DealsService.Business.Events.DealSnapshot</c> — per house
/// convention nothing is shared between services but the transport library, so a producer-side
/// rename lands here as a silent null rather than a build error. See the owning file for the
/// full contract; the notes below are only what the scorer depends on.
///
/// <para><see cref="OccupancyRate"/> and <see cref="MarketCapRateBenchmark"/> are FRACTIONS,
/// not percents: 0.936 is 93.6% occupancy and 0.0609 a 6.09% benchmark. <see cref="TargetIrr"/>
/// follows the same convention. DealScore treats anything above 1.5 as a percent that leaked
/// in and scores it as unknown rather than clamping, because clamping would read a 12% IRR as
/// a 120% one.</para>
///
/// <para><see cref="Version"/>, <see cref="UpdatedAt"/>, <see cref="AiScore"/>,
/// <see cref="AiScoreRationale"/>, <see cref="RiskFlags"/>, <see cref="CommentText"/> and
/// <see cref="DocumentText"/> are all deliberately excluded from the scoring fingerprint —
/// see Fingerprint.ForDealScore, where getting that set wrong means either an infinite
/// rescoring loop or a model call on every comment.</para>
/// </summary>
public record DealSnapshot(
    string DealId,
    long Version,
    string Name,
    string PropertyId,
    string PropertyName,
    string? PropertyType,
    string? MetroArea,
    double? OccupancyRate,
    double? MarketCapRateBenchmark,
    string Stage,
    string Priority,
    string OwnerId,
    string? DeadReason,
    double? OfferPrice,
    double? ProjectedCapRate,
    double? TargetIrr,
    double? EquityMultiple,
    string? ProjectedCloseDate,
    double? AiScore,
    string? AiScoreRationale,
    string? RiskFlags,
    string StageEnteredAt,
    string CreatedAt,
    string? UpdatedAt,
    int TaskCount,
    int DoneTaskCount,
    string? EarliestOpenTaskDueDate,
    double? StageDwellAverageDays,
    int StageDwellSampleCount,
    string? CommentText,
    string? DocumentText,
    bool Deleted);

/// <summary>
/// Published to <see cref="Topics.AiDealScoreReady"/> when a deal's score changes. Consumed by
/// deals-service, which writes it onto the deal row.
///
/// <para><paramref name="Rationale"/> is null when only the number moved, and the consumer
/// leaves the stored prose alone in that case. Slice A never sends one at all: the score is a
/// deterministic formula and free to recompute, while the rationale costs a model call and is
/// regenerated only when the score moves materially.</para>
/// </summary>
public record AiDealScoreReady(
    string DealId,
    double Score,
    string? Rationale);
