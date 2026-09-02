using System.Text.RegularExpressions;

namespace AiService.Business;

/// <summary>
/// The mechanics of a grounded answer, shared by Deal Q&amp;A (§6.4) and the Deal
/// Assistant (§6.8): the [S1] marker the model cites with, the snippet a citation
/// chip shows, and the escaping that stops a file name from breaking out of the
/// delimited block it is announced in.
///
/// <para>Extracted from <see cref="DealQaService"/> rather than copied. Both features
/// have to agree on what a source marker looks like, because both feed markers into a
/// prompt and then read them back out of the answer — and a marker the writer emits
/// but the reader doesn't recognise turns into a silently dropped citation, which is
/// the one failure mode neither feature can detect at runtime.</para>
/// </summary>
public static partial class Grounding
{
    /// <summary>
    /// Matches the [S1] markers the prompts ask Claude to cite with.
    ///
    /// <para>Three digits, not the two Deal Q&amp;A shipped with. Deal Q&amp;A tops out at
    /// <see cref="Retrieval.RetrievalOptions.MaxContextChunks"/> sources, but the assistant
    /// registers a source per tool result across up to six iterations and can pass 99. The
    /// widening is behaviour-neutral for Deal Q&amp;A: an out-of-range marker was previously
    /// not matched at all and is now matched and then dropped for naming a source that
    /// doesn't exist, which is the same outcome by a different route.</para>
    /// </summary>
    [GeneratedRegex(@"\[S(\d{1,3})\]")]
    public static partial Regex SourceMarker();

    /// <summary>
    /// The source numbers an answer actually referenced, de-duplicated and in order.
    ///
    /// <para>Callers still have to reject numbers they never issued. This reports what
    /// the model wrote, not what is real — those are different questions, and conflating
    /// them is how a fabricated marker becomes a citation.</para>
    /// </summary>
    public static IReadOnlyList<int> CitedSourceNumbers(string answer) =>
        [.. SourceMarker().Matches(answer)
             .Select(m => int.Parse(m.Groups[1].Value))
             .Distinct()
             .Order()];

    /// <summary>A short, single-line extract for the citation chip's tooltip.</summary>
    public static string Snippet(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 300 ? flat : flat[..300].TrimEnd() + "…";
    }

    /// <summary>
    /// Makes a value safe to sit inside a double-quoted attribute of the delimited
    /// blocks the prompts use. Quotes become apostrophes rather than entities: the
    /// block is read by a model, not by an XML parser, and a stray <c>&amp;quot;</c>
    /// is noise in a file name it is being asked to repeat back.
    /// </summary>
    public static string Escape(string value) => value.Replace("\"", "'");
}
