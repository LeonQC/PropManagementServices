namespace AiService.Business.Assistant;

/// <summary>
/// What the assistant emits while answering. The controller renders these as SSE frames.
///
/// <para>Progress events exist because the latency is real and unavoidable: a question
/// that runs three tool calls cannot produce answer text in the first second, and a
/// silent five-second wait reads as a broken page. Narrating the steps makes the wait
/// legible instead of hiding it.</para>
/// </summary>
public abstract record AssistantEvent
{
    /// <summary>A tool call is starting.</summary>
    public sealed record Status(int Iteration, string Tool, string Label) : AssistantEvent;

    /// <summary>A tool call finished. <paramref name="Capped"/> means its results were truncated.</summary>
    public sealed record ToolFinished(int Iteration, string Tool, string Summary, bool Capped, bool IsError)
        : AssistantEvent;

    /// <summary>A fragment of answer text, as the model writes it.</summary>
    public sealed record Delta(string Text) : AssistantEvent;

    /// <summary>The sources the finished answer cited.</summary>
    public sealed record Citations(IReadOnlyList<Source> Sources) : AssistantEvent;

    /// <summary>The question is done. <paramref name="Truncated"/> means a budget stopped
    /// the loop before the model chose to stop.</summary>
    public sealed record Done(
        string Model, int Iterations, int ToolCalls, int LatencyMs, bool Truncated) : AssistantEvent;

    /// <summary>The question failed. Carries a stable code so the client can map it.</summary>
    public sealed record Failed(string Code, string Message) : AssistantEvent;
}
