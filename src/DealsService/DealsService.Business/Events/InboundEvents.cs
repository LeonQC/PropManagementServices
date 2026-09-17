namespace DealsService.Business.Events;

/// <summary>
/// Event payloads this service consumes. Local copies of records owned by the publishing
/// service — per house convention nothing is shared between services but the transport
/// library, so the field names here have to match the producer's by hand.
/// </summary>

/// <summary>
/// ai.deal_score_ready — ai-service has scored a deal. Owned by
/// <c>AiService.Business.Events.AiDealScoreReady</c>.
///
/// <para><paramref name="Rationale"/> is null when only the number moved. The score is a
/// deterministic formula and free to recompute, so it is republished on small drifts, while
/// the prose costs a model call and is regenerated only when the score moves materially. A
/// null must therefore leave the stored rationale alone rather than clearing it.</para>
/// </summary>
public record AiDealScoreReady(
    string DealId,
    double Score,
    string? Rationale);
