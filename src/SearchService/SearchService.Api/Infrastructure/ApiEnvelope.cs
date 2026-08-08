namespace SearchService.Api.Infrastructure;

// Response envelope shapes per architecture §5.1, copied from deals-service so
// GET /search/v1/deals is a base-path swap away from GET /deals/v1/deals.
//
// Only the success half is here: this service reads an index and has no ServiceResult, so
// there are no domain errors to shape — a bad request is a 400 from model binding and a
// missing token a 401 from the auth middleware. The properties endpoints don't use any of
// this at all; they return raw JSON, matching listings-service.

public record Meta(string Timestamp, string RequestId);

public record SuccessEnvelope<T>(T Data, Meta Meta);
