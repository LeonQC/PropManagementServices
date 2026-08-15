using AiService.DataAccess;
using Microsoft.Extensions.Logging;

namespace AiService.Business;

/// <summary>
/// Backs the health endpoint. Lives in Business because the Api layer can't reach
/// DataAccess — and because "healthy" here means "able to answer a question", which
/// is a business fact, not an HTTP one.
/// </summary>
public class HealthProbe(
    IPromptTemplateRepository prompts,
    ClaudeClient claude,
    ILogger<HealthProbe> logger)
{
    public async Task<IReadOnlyDictionary<string, string>> RunAsync(CancellationToken ct = default)
    {
        var checks = new Dictionary<string, string>();

        // Reading the active prompt exercises the database connection and confirms the
        // seed ran — one probe for both, and both are prerequisites for answering.
        try
        {
            var template = await prompts.GetActiveAsync(PromptFeatures.DealQa, ct);
            checks["database"] = "ok";
            checks["promptTemplate"] = template is null
                ? $"error: no active prompt for feature '{PromptFeatures.DealQa}'"
                : "ok";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed reading the prompt template.");
            checks["database"] = $"error: {ex.Message}";
            checks["promptTemplate"] = "unknown";
        }

        // Reported rather than called: a live Claude request per health poll would bill
        // real tokens on every container restart and every load-balancer check.
        checks["anthropicApiKey"] = claude.IsConfigured
            ? "ok"
            : "error: no API key configured (set ANTHROPIC_API_KEY)";

        return checks;
    }
}
