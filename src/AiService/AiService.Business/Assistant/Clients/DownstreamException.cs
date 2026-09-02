namespace AiService.Business.Assistant.Clients;

/// <summary>
/// A downstream service refused or failed a tool's request.
///
/// <para>These are reported back to the model as tool errors rather than thrown out of
/// the loop. A 403 on one deal is a fact the assistant should state — "I don't have
/// access to that deal" — not a reason to abandon a question that may have five other
/// tool calls succeeding beside it. <see cref="Denied"/> marks the authorization case
/// so the message the model sees says so plainly.</para>
/// </summary>
public class DownstreamException(string message, bool denied = false, Exception? inner = null)
    : Exception(message, inner)
{
    public bool Denied { get; } = denied;
}
