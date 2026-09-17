using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AiService.Business.Events;

namespace AiService.Business.Workers;

/// <summary>
/// A content hash over exactly the fields that feed a piece of AI work, used to decide whether
/// that work needs doing again. Pure and allocation-light; no I/O.
///
/// <para><b>This is what stops the feedback loop.</b> Writing a score back makes deals-service
/// bump <c>Version</c> and republish <c>deal.snapshot</c>, which arrives here again. Because
/// the fields our own write-back mutates are excluded, the echo hashes identically to the
/// message that triggered us and the gate skips it. Add <c>Version</c>, <c>UpdatedAt</c> or any
/// <c>Ai*</c> column to a fingerprint and the worker will rescore forever.</para>
///
/// <para><b>It is also the cost gate.</b> <c>deal.snapshot</c> republishes on every comment,
/// task edit and document upload. Those change <see cref="DealSnapshot.CommentText"/> and
/// <see cref="DealSnapshot.DocumentText"/>, which are excluded, so they exit after one
/// database lookup instead of reaching a model.</para>
/// </summary>
public static class Fingerprint
{
    // Bump when the field set changes. A deliberate global invalidation: every entity
    // regenerates exactly once, rather than silently keeping work done against old inputs.
    private const string DealScoreSchema = "dscore.v1";

    // ASCII unit separator, written as an escape because it is invisible in source.
    // Positional framing means a null and an empty string hash alike, but neither can
    // shift the following fields into the wrong slots.
    private const char FieldSeparator = '\u001F';

    /// <summary>
    /// The scoring inputs of a deal, and nothing else. Every field here is read by
    /// <see cref="DealScore.Compute"/> or by the rationale prompt; every field absent from
    /// here is one whose change must not cost anything.
    /// </summary>
    public static string ForDealScore(DealSnapshot d)
    {
        var sb = new StringBuilder(256);

        Append(sb, DealScoreSchema);
        Append(sb, d.DealId);
        Append(sb, d.Name);
        Append(sb, d.PropertyName);
        Append(sb, d.PropertyType);
        Append(sb, d.MetroArea);
        Append(sb, d.Stage);
        Append(sb, d.StageEnteredAt);
        Append(sb, d.OfferPrice);
        Append(sb, d.ProjectedCapRate);
        Append(sb, d.TargetIrr);
        Append(sb, d.EquityMultiple);
        Append(sb, d.OccupancyRate);
        Append(sb, d.MarketCapRateBenchmark);
        Append(sb, d.ProjectedCloseDate);
        Append(sb, d.TaskCount);
        Append(sb, d.DoneTaskCount);
        Append(sb, d.StageDwellAverageDays);
        Append(sb, d.StageDwellSampleCount);

        return Hash(sb.ToString());
    }

    /// <summary>Lowercase hex SHA-256. Not a security boundary — this only has to be stable
    /// across restarts and collision-free in practice over a few hundred deals.</summary>
    private static string Hash(string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

    private static void Append(StringBuilder sb, string? value)
    {
        // Trimmed but NOT lowercased: a case change is a real content change.
        sb.Append(value?.Trim());
        sb.Append(FieldSeparator);
    }

    /// <summary>
    /// Rounded before formatting because a double that round-trips through JSON can come back
    /// perturbed in its last bits, and InvariantCulture because a container with a comma
    /// decimal separator would otherwise invalidate every stored fingerprint at once — a
    /// failure that costs a full rescore and shows up nowhere in the logs.
    /// </summary>
    private static void Append(StringBuilder sb, double? value)
    {
        if (value is not null)
            sb.Append(Math.Round(value.Value, 6).ToString("0.######", CultureInfo.InvariantCulture));
        sb.Append(FieldSeparator);
    }

    private static void Append(StringBuilder sb, int value)
    {
        sb.Append(value.ToString(CultureInfo.InvariantCulture));
        sb.Append(FieldSeparator);
    }
}
