using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiService.Business.Assistant.Tools;

/// <summary>
/// Reads and validates the arguments a tool call arrived with.
///
/// <para>These are <b>model-generated</b> values, not user form input, and that is the
/// reason this exists rather than a plain deserialize. The model's output is downstream of
/// text it was shown, some of which came out of user-uploaded PDFs — so an argument is the
/// one place where an injected instruction could try to reach a downstream query string.
/// Every value is therefore read through a typed accessor with an explicit range or an
/// explicit vocabulary; nothing is passed through on trust.</para>
///
/// <para>Two deliberate leniencies, both because the alternative is a wasted iteration
/// rather than a safer call: a value that is right but wrongly cased ("industrial") is
/// normalised to the canonical form, and a number sent as a string ("0.065") is parsed.
/// Being strict about those buys nothing — the model would simply retry — while being
/// strict about the <i>vocabulary</i> buys everything, because an invented filter value
/// matches nothing downstream and returns an empty result the model reports as fact.</para>
/// </summary>
public sealed class ToolArguments
{
    private readonly string _tool;
    private readonly JsonObject _arguments;

    private ToolArguments(string tool, JsonObject arguments)
    {
        _tool = tool;
        _arguments = arguments;
    }

    /// <summary>
    /// Parses the call's input and rejects any property the tool does not declare.
    /// Unknown properties are an error rather than an ignored extra: silently dropping one
    /// means the model believes it applied a filter that never reached the query, and the
    /// narrowed answer it writes is then wrong in a way nothing surfaces.
    /// </summary>
    public static ToolArguments Read(string tool, JsonNode? input, params string[] accepted)
    {
        if (input is null) return new ToolArguments(tool, []);

        if (input is not JsonObject arguments)
            throw new ToolArgumentException(
                $"The arguments for '{tool}' must be a JSON object with named fields.");

        var unknown = arguments.Select(pair => pair.Key)
            .Where(key => !accepted.Contains(key, StringComparer.Ordinal))
            .ToList();

        if (unknown.Count > 0)
            throw new ToolArgumentException(
                $"'{tool}' does not accept {string.Join(", ", unknown.Select(u => $"'{u}'"))}. " +
                (accepted.Length == 0
                    ? "It takes no arguments."
                    : $"Accepted arguments: {string.Join(", ", accepted)}."));

        return new ToolArguments(tool, arguments);
    }

    /// <summary>Free text, trimmed and length-capped. Null when absent or blank.</summary>
    public string? Text(string name, int maxLength = 200)
    {
        if (Node(name) is not { } node) return null;

        var value = node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : throw Wrong(name, "a string");

        value = value.Trim();
        if (value.Length == 0) return null;

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>
    /// One of a fixed vocabulary, matched case-insensitively and returned in its canonical
    /// casing — so "industrial" becomes "Industrial" rather than matching nothing.
    /// </summary>
    public string? OneOf(string name, IReadOnlyList<string> allowed)
    {
        if (Text(name) is not { } value) return null;

        var match = allowed.FirstOrDefault(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ToolArgumentException(
            $"'{value}' is not a valid {name} for '{_tool}'. It must be one of: {string.Join(", ", allowed)}.");
    }

    /// <summary>A number inside an inclusive range. Accepts a numeric string too.</summary>
    public double? Number(string name, double min, double max)
    {
        if (Node(name) is not { } node) return null;

        var value = node.GetValueKind() switch
        {
            JsonValueKind.Number => node.GetValue<double>(),
            JsonValueKind.String when double.TryParse(
                node.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw Wrong(name, "a number"),
        };

        if (value < min || value > max)
            throw new ToolArgumentException(
                $"'{name}' for '{_tool}' must be between {min} and {max}; got {value.ToString(CultureInfo.InvariantCulture)}.");

        return value;
    }

    /// <summary>A count, clamped rather than rejected — an over-large page size is a
    /// preference the server is entitled to overrule, not a malformed call.</summary>
    public int Count(string name, int fallback, int min, int max)
    {
        if (Node(name) is not { } node) return fallback;

        var value = node.GetValueKind() switch
        {
            JsonValueKind.Number => node.GetValue<int>(),
            JsonValueKind.String when int.TryParse(
                node.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw Wrong(name, "an integer"),
        };

        return Math.Clamp(value, min, max);
    }

    public bool? Flag(string name)
    {
        if (Node(name) is not { } node) return null;

        return node.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(node.GetValue<string>(), out var parsed) => parsed,
            _ => throw Wrong(name, "true or false"),
        };
    }

    /// <summary>
    /// A yyyy-MM-dd date. The downstream filters compare these as ordinal strings, so a
    /// value in any other format does not error there — it silently orders wrong.
    /// </summary>
    public string? Date(string name)
    {
        if (Text(name) is not { } value) return null;

        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                      DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new ToolArgumentException(
                $"'{name}' for '{_tool}' must be a date formatted yyyy-MM-dd; got '{value}'.");
    }

    /// <summary>A required identifier — the one argument shape where absence is fatal.</summary>
    public string Required(string name, int maxLength = 200) =>
        Text(name, maxLength) ?? throw new ToolArgumentException(
            $"'{name}' is required for '{_tool}'.");

    private JsonNode? Node(string name) =>
        _arguments.TryGetPropertyValue(name, out var node) && node is not null
        && node.GetValueKind() != JsonValueKind.Null
            ? node
            : null;

    private ToolArgumentException Wrong(string name, string expected) =>
        new($"'{name}' for '{_tool}' must be {expected}.");
}
