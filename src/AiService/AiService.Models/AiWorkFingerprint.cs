namespace AiService.Models;

/// <summary>
/// A record that a piece of AI work has already been done for one entity, and against which
/// inputs. Keyed by (Feature, EntityId).
///
/// <para>This table is what makes the scoring worker idempotent. The snapshot topics are
/// compacted and consumed from the earliest offset, so a fresh consumer group replays every
/// deal on startup; each replay finds a matching fingerprint here and exits without work. It
/// is also what breaks the write-back feedback loop — see Fingerprint for the field set and
/// why its exclusions are load-bearing.</para>
/// </summary>
public class AiWorkFingerprint
{
    /// <summary>Which piece of work. Matches a PromptFeatures constant.</summary>
    public required string Feature { get; set; }

    /// <summary>The deal or property this covers.</summary>
    public required string EntityId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the inputs, as of the last completed run.</summary>
    public required string InputFingerprint { get; set; }

    /// <summary>
    /// The score the currently stored rationale prose was written at. Null when no prose has
    /// been generated — which is every row until the rationale slice ships. Kept separate from
    /// the deal's present score on purpose: it is the baseline for deciding the prose has gone
    /// stale, and using the present score instead lets a run of small drifts move the number
    /// arbitrarily far without ever triggering a regeneration.
    /// </summary>
    public double? LastOutputScore { get; set; }

    /// <summary>ISO-8601 round-trip, matching AiRequestLog.CreatedAt.</summary>
    public required string ComputedAt { get; set; }
}
