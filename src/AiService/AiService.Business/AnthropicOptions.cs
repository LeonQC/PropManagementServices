namespace AiService.Business;

/// <summary>
/// Claude settings, bound from the "Anthropic" config section. The key comes from
/// the environment (compose passes ${ANTHROPIC_API_KEY} from the untracked .env)
/// or user-secrets locally — never from appsettings.json.
/// </summary>
public class AnthropicOptions
{
    public string ApiKey { get; set; } = "";

    /// <summary>Model id as a plain string rather than one of the SDK's
    /// <c>AnthropicModels</c> constants: that list is pinned to the SDK release and
    /// has no entry for claude-sonnet-5, so hard-coding a constant would pin us to
    /// an older model than the one we mean to call.</summary>
    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>Answers are grounded summaries of retrieved text, not open-ended
    /// prose — a few hundred tokens is plenty, and the cap bounds the cost of a
    /// question that invites a wandering answer.</summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>
    /// Omitted by default: newer models (claude-sonnet-5 among them) reject
    /// <c>temperature</c> outright — "`temperature` is deprecated for this model" —
    /// so sending even a sensible value fails the whole request. Left configurable
    /// because older models still accept it, but null means "don't send the field".
    /// </summary>
    public decimal? Temperature { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>USD per million input tokens, for the cost column on ai_request_log.
    /// Configurable so a price change is a config edit, and recorded per row at call
    /// time so history isn't retroactively repriced.</summary>
    public double InputCostPerMillionTokens { get; set; } = 3.0;

    /// <summary>USD per million output tokens.</summary>
    public double OutputCostPerMillionTokens { get; set; } = 15.0;
}

/// <summary>Ingestion-service connection settings, bound from the "Ingestion" section.</summary>
public class IngestionOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5500";
    public int TimeoutSeconds { get; set; } = 30;
}
