using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AiService.Business.Workers;

/// <summary>What a completion cost and how long it took, for the request ledger.</summary>
public record RationaleCompletion(string Text, int InputTokens, int OutputTokens, int LatencyMs);

/// <summary>A model call that failed. Wraps the cause so the retry policy can classify it.</summary>
public class RationaleException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>LiteLLM proxy settings, bound from the "LiteLlm" section.</summary>
public class LiteLlmOptions
{
    public string BaseUrl { get; set; } = "http://localhost:4000";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Writes the prose explaining a deal's score, through the LiteLLM proxy's OpenAI-compatible
/// chat endpoint.
///
/// <para>Deliberately not the Anthropic SDK that Deal Q&amp;A and the assistant use. Routing
/// generation through the proxy that already fronts embeddings, reranking and the eval judge
/// makes the model a config value rather than a code dependency, so comparing candidates — or
/// moving this to a local model later — costs one line in litellm/config.yaml.</para>
///
/// <para>Raw JSON over HttpClient rather than an SDK, matching IngestionSearchClient and
/// DealRecordClient. The request is four fields and the response is one string; an SDK would
/// be a dependency for nothing.</para>
/// </summary>
public class RationaleClient(HttpClient http, ILogger<RationaleClient> logger)
{
    private record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] ChatMessage[] Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);

    private record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);

    private record ChatResponse(
        [property: JsonPropertyName("choices")] List<ChatChoice>? Choices,
        [property: JsonPropertyName("usage")] ChatUsage? Usage);

    /// <summary>
    /// One completion. Throws <see cref="RationaleException"/> on any failure, with the cause
    /// as the inner exception so ModelCallPolicy can tell a transient fault from a permanent
    /// one. Cancellation is left unwrapped, because a shutdown is not a failure to retry.
    /// </summary>
    public async Task<RationaleCompletion> CompleteAsync(
        string model, string systemPrompt, string userMessage, int maxTokens, CancellationToken ct = default)
    {
        var startedAt = Environment.TickCount64;

        try
        {
            var request = new ChatRequest(model,
                [new ChatMessage("system", systemPrompt), new ChatMessage("user", userMessage)],
                maxTokens);

            var response = await http.PostAsJsonAsync("/v1/chat/completions", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                // The status code goes in the message because the policy classifies on it —
                // a 429 is worth retrying and a 400 never is.
                throw new RationaleException(
                    $"LiteLLM returned {(int)response.StatusCode}: {Truncate(body, 400)}");
            }

            var parsed = await response.Content.ReadFromJsonAsync<ChatResponse>(ct);
            var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content;

            if (string.IsNullOrWhiteSpace(text))
                throw new RationaleException("LiteLLM returned no completion text.");

            var latencyMs = (int)(Environment.TickCount64 - startedAt);
            logger.LogDebug("Rationale completion from {Model} in {LatencyMs}ms", model, latencyMs);

            return new RationaleCompletion(
                text.Trim(),
                parsed!.Usage?.PromptTokens ?? 0,
                parsed.Usage?.CompletionTokens ?? 0,
                latencyMs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RationaleException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RationaleException($"Rationale call to {model} failed: {ex.Message}", ex);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
