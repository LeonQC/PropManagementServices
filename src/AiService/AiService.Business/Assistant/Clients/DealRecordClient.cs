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
