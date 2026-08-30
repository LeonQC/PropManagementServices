using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AiService.Business.Assistant.Clients;

/// <summary>A deterministic health signal on a deal (design doc §6.6), computed per
/// request by whichever service served it.</summary>
public record HealthFlag(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("message")] string Message);

/// <summary>
/// A deal as both deals-service and search-service return it — the two endpoints share a
/// field-for-field identical DTO on purpose, so one record deserialises either.
///
/// <para><see cref="ProjectedCapRate"/> and <see cref="OccupancyRate"/> are
/// <b>fractions</b>: 0.065 is 6.5%. Every tool description that exposes a cap-rate filter
/// has to say so, because a model that passes 6.5 gets an empty result rather than an
/// error.</para>
/// </summary>
public record DealRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("propertyId")] string? PropertyId,
    [property: JsonPropertyName("propertyName")] string? PropertyName,
    [property: JsonPropertyName("propertyType")] string? PropertyType,
    [property: JsonPropertyName("metroArea")] string? MetroArea,
    [property: JsonPropertyName("occupancyRate")] double? OccupancyRate,
    [property: JsonPropertyName("marketCapRateBenchmark")] double? MarketCapRateBenchmark,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("priority")] string? Priority,
    [property: JsonPropertyName("ownerId")] string? OwnerId,
    [property: JsonPropertyName("deadReason")] string? DeadReason,
    [property: JsonPropertyName("offerPrice")] double? OfferPrice,
    [property: JsonPropertyName("projectedCapRate")] double? ProjectedCapRate,
    [property: JsonPropertyName("targetIrr")] double? TargetIrr,
    [property: JsonPropertyName("equityMultiple")] double? EquityMultiple,
    [property: JsonPropertyName("projectedCloseDate")] string? ProjectedCloseDate,
    [property: JsonPropertyName("aiScore")] double? AiScore,
    [property: JsonPropertyName("stageEnteredAt")] string? StageEnteredAt,
    [property: JsonPropertyName("createdAt")] string? CreatedAt,
    [property: JsonPropertyName("updatedAt")] string? UpdatedAt,
    [property: JsonPropertyName("taskCount")] int TaskCount,
    [property: JsonPropertyName("doneTaskCount")] int DoneTaskCount,
    [property: JsonPropertyName("hasOverdueTasks")] bool HasOverdueTasks,
    [property: JsonPropertyName("healthFlags")] List<HealthFlag>? HealthFlags);

public record Address(
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("metroArea")] string? MetroArea,
    [property: JsonPropertyName("neighborhood")] string? Neighborhood);

public record PropertyRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("propertyType")] string? PropertyType,
    [property: JsonPropertyName("propertySubtype")] string? PropertySubtype,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("totalSqft")] double? TotalSqft,
    [property: JsonPropertyName("leasableSqft")] double? LeasableSqft,
    [property: JsonPropertyName("yearBuilt")] int? YearBuilt,
    [property: JsonPropertyName("unitCount")] int? UnitCount,
    [property: JsonPropertyName("askingPrice")] double? AskingPrice,
    [property: JsonPropertyName("capRate")] double? CapRate,
    [property: JsonPropertyName("noi")] double? Noi,
    [property: JsonPropertyName("occupancyRate")] double? OccupancyRate,
    [property: JsonPropertyName("address")] Address? Address,
    [property: JsonPropertyName("listedAt")] string? ListedAt);

/// <summary>One cross-entity hit from GET /search/v1/all.</summary>
public record SearchHit(
    [property: JsonPropertyName("entityType")] string EntityType,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("snippet")] string? Snippet,
    [property: JsonPropertyName("score")] double Score);

/// <summary>A page of results, plus the total the filter actually matched. The gap
/// between the two is what the candidate cap is built on — an answer must be able to say
/// it examined ten of forty.</summary>
public record Page<T>(
    [property: JsonPropertyName("items")] List<T> Items,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("page")] int PageNumber,
    [property: JsonPropertyName("pageSize")] int PageSize);

/// <summary>
/// Reads OpenSearch-backed deal, property and cross-entity search from search-service.
///
/// <para>Backed by search-service rather than the equivalent Postgres endpoints on
/// deals-service and listings-service: the signatures are identical, and OpenSearch gives
/// better recall and typo tolerance on the free-text half, which is most of what a model
/// sends.</para>
///
/// <para>Two shape traps, both verified against the controllers rather than assumed.
/// <c>GET /search/v1/deals</c> and <c>/all</c> are enveloped in <c>{data, meta}</c>, but
/// <c>GET /search/v1/properties</c> is <b>not</b> — it returns the page object bare,
/// mirroring listings-service. And search-service's <c>ApiControllerBase</c> has no error
/// mapping at all, so an upstream fault arrives as a naked 500; it has to be treated as a
/// tool failure rather than as an empty result, or the assistant reports "no matches"
/// when the truth is "the search engine is down".</para>
/// </summary>
public class SearchClient(HttpClient http, ILogger<SearchClient> logger)
{
    private record Envelope<T>(T Data);

    public Task<Page<DealRecord>> SearchDealsAsync(
        IReadOnlyList<(string Key, string Value)> filters, string bearerToken, CancellationToken ct = default) =>
        GetEnvelopedAsync<Page<DealRecord>>(Url("/search/v1/deals", filters), bearerToken, "deal search", ct);

    public Task<Page<SearchHit>> SearchAllAsync(
        IReadOnlyList<(string Key, string Value)> filters, string bearerToken, CancellationToken ct = default) =>
        GetEnvelopedAsync<Page<SearchHit>>(Url("/search/v1/all", filters), bearerToken, "cross-entity search", ct);

    /// <summary>
    /// Properties. Deliberately not unwrapped: this endpoint returns the page object
    /// directly, unlike its two neighbours.
    /// </summary>
    public async Task<Page<PropertyRecord>> SearchPropertiesAsync(
        IReadOnlyList<(string Key, string Value)> filters, string bearerToken, CancellationToken ct = default)
    {
        using var response = await SendAsync(Url("/search/v1/properties", filters), bearerToken, "property search", ct);
        var page = await response.Content.ReadFromJsonAsync<Page<PropertyRecord>>(ct);
        return page ?? throw new DownstreamException("The search service returned an empty property search result.");
    }

    private async Task<T> GetEnvelopedAsync<T>(string url, string bearerToken, string what, CancellationToken ct)
    {
        using var response = await SendAsync(url, bearerToken, what, ct);
        var body = await response.Content.ReadFromJsonAsync<Envelope<T>>(ct);
        return body is { Data: not null }
            ? body.Data
            : throw new DownstreamException($"The search service returned an empty {what} result.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        string url, string bearerToken, string what, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearerToken}");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Request to search-service for {What} failed.", what);
            throw new DownstreamException($"Could not reach the search service to run the {what}.", inner: ex);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            logger.LogWarning("search-service rejected the caller's token running the {What}.", what);
            throw new DownstreamException($"You do not have access to run the {what}.", denied: true);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            logger.LogError("search-service returned {Status} running the {What}.", status, what);
            throw new DownstreamException($"The {what} failed ({(int)status}).");
        }

        return response;
    }

    /// <summary>Builds the query string. Every value is escaped here, which is the last
    /// point where a model-supplied string is still a value rather than a URL.</summary>
    private static string Url(string path, IReadOnlyList<(string Key, string Value)> filters) =>
        filters.Count == 0
            ? path
            : $"{path}?{string.Join('&', filters.Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}"))}";
}
