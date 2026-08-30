using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AiService.Business.Assistant.Clients;

/// <summary>One stage's slice of the pipeline.</summary>
public record PipelineStage(
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("totalValue")] double TotalValue);

/// <summary>
/// Deal counts and value by stage. <paramref name="TotalActiveDeals"/> and
/// <paramref name="TotalPipelineValue"/> exclude the terminal stages (Acquired, Dead),
/// while <paramref name="Stages"/> lists all six — so the totals deliberately do not
/// equal the sum of the rows, and the tool description has to say so or the model will
/// "correct" the discrepancy.
/// </summary>
public record PipelineSummary(
    [property: JsonPropertyName("totalActiveDeals")] int TotalActiveDeals,
    [property: JsonPropertyName("totalPipelineValue")] double TotalPipelineValue,
    [property: JsonPropertyName("stages")] List<PipelineStage> Stages);

/// <summary>One stage transition. Ownership transfers are recorded as same-stage rows
/// whose <paramref name="Reason"/> is "OWNER_TRANSFER:{from}:{to}" — they are history, not
/// stage movement, and reading them as stalling would be wrong.</summary>
public record StageChange(
    [property: JsonPropertyName("fromStage")] string? FromStage,
    [property: JsonPropertyName("toStage")] string ToStage,
    [property: JsonPropertyName("changedById")] string? ChangedById,
    [property: JsonPropertyName("changedAt")] string ChangedAt,
    [property: JsonPropertyName("daysInStage")] int? DaysInStage,
    [property: JsonPropertyName("reason")] string? Reason);

public record DealTask(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("stage")] string? Stage,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("assigneeId")] string? AssigneeId,
    [property: JsonPropertyName("dueDate")] string? DueDate,
    [property: JsonPropertyName("completedAt")] string? CompletedAt);

/// <summary>A comment on a deal. <paramref name="Body"/> is user-authored free text and
/// is treated as untrusted, exactly like a document excerpt.</summary>
public record DealComment(
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("authorId")] string? AuthorId,
    [property: JsonPropertyName("isAiGenerated")] bool IsAiGenerated,
    [property: JsonPropertyName("createdAt")] string CreatedAt);

/// <summary>
/// Reads structured deal data from deals-service on behalf of the assistant's tools.
///
/// <para>Carries the caller's own bearer token on every request, like every other
/// outbound call in this service. ai-service holds no service account and no database
/// credentials, so a question can never reach a deal the user couldn't already fetch —
/// see IngestionSearchClient for why adding one "for convenience" would widen the blast
/// radius of the assistant to the entire portfolio.</para>
/// </summary>
public class DealRecordClient(HttpClient http, ILogger<DealRecordClient> logger)
{
    private record Envelope<T>(T Data);

    public Task<PipelineSummary> GetPipelineSummaryAsync(string bearerToken, CancellationToken ct = default) =>
        GetAsync<PipelineSummary>("/deals/v1/pipeline/summary", bearerToken, "pipeline summary", ct);

    public Task<DealRecord> GetDealAsync(string dealId, string bearerToken, CancellationToken ct = default) =>
        GetAsync<DealRecord>($"/deals/v1/deals/{Uri.EscapeDataString(dealId)}", bearerToken, "deal", ct);

    // The three sub-resources return `data` as a bare array rather than a page object.
    public Task<List<StageChange>> GetHistoryAsync(string dealId, string bearerToken, CancellationToken ct = default) =>
        GetAsync<List<StageChange>>($"/deals/v1/deals/{Uri.EscapeDataString(dealId)}/history", bearerToken, "stage history", ct);

    public Task<List<DealTask>> GetTasksAsync(string dealId, string bearerToken, CancellationToken ct = default) =>
        GetAsync<List<DealTask>>($"/deals/v1/deals/{Uri.EscapeDataString(dealId)}/tasks", bearerToken, "tasks", ct);

    public Task<List<DealComment>> GetCommentsAsync(string dealId, string bearerToken, CancellationToken ct = default) =>
        GetAsync<List<DealComment>>($"/deals/v1/deals/{Uri.EscapeDataString(dealId)}/comments", bearerToken, "comments", ct);

    private async Task<T> GetAsync<T>(string path, string bearerToken, string what, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearerToken}");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Request to deals-service for the {What} failed.", what);
            throw new DownstreamException($"Could not reach the deals service to read the {what}.", inner: ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                logger.LogWarning("deals-service rejected the caller's token ({Status}) reading the {What}.",
                    response.StatusCode, what);
                throw new DownstreamException($"You do not have access to the {what}.", denied: true);
            }

            // A 404 is an answer, not a fault: the deal does not exist. Saying so is more
            // useful than "reading the deal failed (404)", and it stops the model retrying
            // an id that will never resolve.
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new DownstreamException($"No such {what} exists. Check the id before trying again.");

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("deals-service returned {Status} reading the {What}.", response.StatusCode, what);
                throw new DownstreamException($"Reading the {what} failed ({(int)response.StatusCode}).");
            }

            var body = await response.Content.ReadFromJsonAsync<Envelope<T>>(ct);
            return body is { Data: not null }
                ? body.Data
                : throw new DownstreamException($"The deals service returned an empty {what}.");
        }
    }
}
