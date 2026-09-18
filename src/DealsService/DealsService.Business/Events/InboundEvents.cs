namespace DealsService.Business.Events;

/// <summary>
/// Event payloads this service consumes. Local copies of records owned by the publishing
/// service — per house convention nothing is shared between services but the transport
/// library, so the field names here have to match the producer's by hand.
/// </summary>

/// <summary>
/// ai.deal_rationale_ready — ai-service has written prose explaining a deal's score. Owned by
/// <c>AiService.Business.Events.AiDealRationaleReady</c>.
///
/// <para>Carries no score. The number is a deterministic formula computed on read in this
/// service, so there is nothing to write back and nothing that could fall out of sync; only
/// the rationale crosses the boundary, because only the rationale costs a model call.</para>
/// </summary>
public record AiDealRationaleReady(
    string DealId,
    string Rationale);
