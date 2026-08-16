namespace AiService.Api.Infrastructure;

// Response envelope shapes per architecture §5.1, matching deals-service so the UI's
// existing client helpers unwrap these without a special case.

public record Meta(string Timestamp, string RequestId);

public record SuccessEnvelope<T>(T Data, Meta Meta);

public record FieldErrorResponse(string Field, string Message);

public record ErrorBody(
    string Code,
    string Message,
    IReadOnlyList<FieldErrorResponse> Details,
    string RequestId,
    string Timestamp);

public record ErrorEnvelope(ErrorBody Error);
