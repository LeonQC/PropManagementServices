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

    /// <summary>
    /// The model the Deal Assistant's tool-use loop runs on, kept separate from
    /// <see cref="Model"/> so the two features can diverge.
    ///
    /// <para>Deal Q&amp;A is one call over retrieved text and Sonnet handles it well.
    /// The assistant sequences tool calls, has to keep "structured filter before
    /// document search" straight across six iterations, and has to volunteer that its
    /// candidate set was capped — instruction-following under a long horizon, which is
    /// what the stronger model buys. Flipping this is an env var, not a deploy.</para>
    /// </summary>
    public string AssistantModel { get; set; } = "claude-opus-5";

    /// <summary>Output cap for an assistant turn. Larger than <see cref="MaxTokens"/>:
    /// Deal Q&amp;A answers one question from one retrieval, while an assistant answer may
    /// have to report on ten deals and disclose what it did not check, and a cap that
    /// truncates that mid-sentence would look like the model losing the thread.</summary>
    public int AssistantMaxTokens { get; set; } = 4096;

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>USD per million input tokens, for the cost column on ai_request_log.
    /// Configurable so a price change is a config edit, and recorded per row at call
    /// time so history isn't retroactively repriced.</summary>
    public double InputCostPerMillionTokens { get; set; } = 3.0;

    /// <summary>USD per million output tokens.</summary>
    public double OutputCostPerMillionTokens { get; set; } = 15.0;

    /// <summary>Per-million rates for <see cref="AssistantModel"/>. Separate fields
    /// rather than a lookup: the ledger has to price a row at call time, and two
    /// features on two models with one price pair would silently misreport whichever
    /// one didn't own the numbers.</summary>
    public double AssistantInputCostPerMillionTokens { get; set; } = 5.0;

    public double AssistantOutputCostPerMillionTokens { get; set; } = 25.0;
}

/// <summary>Ingestion-service connection settings, bound from the "Ingestion" section.</summary>
public class IngestionOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5500";
    public int TimeoutSeconds { get; set; } = 30;
}
