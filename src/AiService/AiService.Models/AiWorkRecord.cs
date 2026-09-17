namespace AiService.Models;

/// <summary>
/// What this service last produced for one entity, and at what point. Keyed by
/// (Feature, EntityId).
///
/// <para>Exists to stop a high-frequency topic turning into a bill. deal.snapshot is
/// republished on every comment, task edit and document upload, and without a record of what
/// was already written the worker would regenerate prose on each one. See DealRationaleGate.</para>
/// </summary>
public class AiWorkRecord
{
    /// <summary>Which piece of work. Matches a PromptFeatures constant.</summary>
    public required string Feature { get; set; }

    /// <summary>The deal or property this covers.</summary>
    public required string EntityId { get; set; }

    /// <summary>
    /// The score the currently stored prose was written to explain.
    ///
    /// <para>Kept separate from the deal's present score on purpose: it is the baseline for
    /// judging the prose stale. Comparing against the present score instead lets a run of
    /// small drifts move the number arbitrarily far while the gap never opens, leaving a
    /// sentence describing a 60 attached to a score of 48.</para>
    /// </summary>
    public double? LastOutputScore { get; set; }

    /// <summary>ISO-8601 round-trip, matching AiRequestLog.CreatedAt.</summary>
    public required string ComputedAt { get; set; }
}
