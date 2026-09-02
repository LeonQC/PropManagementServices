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

    /// <summary>
    /// USD per million tokens, keyed by model id. Recorded per row at call time so a
    /// later price change never retroactively reprices history.
    ///
    /// <para>Keyed by <b>model</b>, not by feature. The first cut of this had one price
    /// pair per feature, which was wrong in a way that only showed up under measurement:
    /// pointing <see cref="AssistantModel"/> at Sonnet while the assistant's rates stayed
    /// pinned to Opus priced every Sonnet row at 2.5x its real cost. Nothing errored — the
    /// ledger just quietly reported the wrong number, which is the failure mode a cost
    /// ledger exists to prevent. A rate belongs to whatever the model actually was.</para>
    /// </summary>
    public Dictionary<string, ModelRate> ModelRates { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-5"] = new(5.0, 25.0),
        ["claude-sonnet-5"] = new(2.0, 10.0),
        ["claude-haiku-4-5"] = new(1.0, 5.0),
    };

    /// <summary>Rates used when a model has no entry in <see cref="ModelRates"/>. Deliberately
    /// the most expensive tier: an unpriced model should over-report rather than under-report,
    /// because a bill that looks too low is one nobody investigates.</summary>
    public ModelRate FallbackRate { get; set; } = new(5.0, 25.0);

    /// <summary>The rates for one model, or <see cref="FallbackRate"/> when it is unknown.</summary>
    public ModelRate RatesFor(string model) =>
        ModelRates.TryGetValue(model, out var rate) ? rate : FallbackRate;
}

/// <summary>USD per million tokens for one model. A record so a rate is replaced whole
/// rather than half-updated when prices change.</summary>
public record ModelRate(double InputPerMillion, double OutputPerMillion)
{
    // Parameterless ctor so the configuration binder can construct it from appsettings.
    public ModelRate() : this(0, 0) { }
}

/// <summary>Ingestion-service connection settings, bound from the "Ingestion" section.</summary>
public class IngestionOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5500";
    public int TimeoutSeconds { get; set; } = 30;
}
