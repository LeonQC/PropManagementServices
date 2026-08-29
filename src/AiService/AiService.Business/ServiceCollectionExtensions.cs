using AiService.Business.Assistant;
using AiService.Business.Assistant.Clients;
using AiService.Business.Assistant.Tools;
using AiService.Business.Retrieval;
using AiService.Business.Security;
using AiService.DataAccess;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AiService.Business;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusiness(this IServiceCollection services, IConfiguration config)
    {
        services.AddDataAccess(config.GetConnectionString("AiDb")
            ?? throw new InvalidOperationException("ConnectionStrings:AiDb is not configured."));

        services.Configure<AnthropicOptions>(config.GetSection("Anthropic"));
        services.Configure<IngestionOptions>(config.GetSection("Ingestion"));
        services.Configure<RetrievalOptions>(config.GetSection("Retrieval"));
        services.Configure<DealsOptions>(config.GetSection("Deals"));
        services.Configure<SearchOptions>(config.GetSection("Search"));
        services.Configure<AssistantOptions>(config.GetSection("Assistant"));

        var ingestion = config.GetSection("Ingestion").Get<IngestionOptions>() ?? new IngestionOptions();
        services.AddHttpClient<IngestionSearchClient>(client =>
        {
            client.BaseAddress = new Uri(ingestion.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(ingestion.TimeoutSeconds);
        });

        var deals = config.GetSection("Deals").Get<DealsOptions>() ?? new DealsOptions();
        services.AddHttpClient<DealDocumentsClient>(client =>
        {
            client.BaseAddress = new Uri(deals.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(deals.TimeoutSeconds);
        });

        services.AddHttpClient<DealRecordClient>(client =>
        {
            client.BaseAddress = new Uri(deals.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(deals.TimeoutSeconds);
        });

        services.AddScoped<RetrievalService>();
        services.AddScoped<RequestLedger>();
        services.AddScoped<ClaudeClient>();
        services.AddScoped<DealQaService>();
        services.AddScoped<HealthProbe>();

        // Tools are registered against the interface so ToolDispatcher discovers them by
        // enumeration: adding a tool is one registration here and nothing else.
        services.AddScoped<IAssistantTool, PipelineSummaryTool>();
        services.AddScoped<ToolDispatcher>();
        services.AddScoped<AssistantService>();

        services.Configure<JwtValidationOptions>(config.GetSection("Jwt"));
        AddJwtBearerAuth(services);

        return services;
    }

    /// <summary>
    /// Registers JWT bearer validation against auth-service-issued RS256 tokens.
    /// Validation parameters mirror the auth-service's own, except the signing key
    /// is resolved from its JWKS endpoint via <see cref="JwksSigningKeyCache"/>
    /// (auth publishes raw JWKS, not an OIDC discovery document).
    ///
    /// Every endpoint here requires a token: the answer is built from a deal's
    /// documents, and the same token is forwarded downstream to fetch them.
    /// </summary>
    private static void AddJwtBearerAuth(IServiceCollection services)
    {
        services.AddSingleton<JwksSigningKeyCache>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwksSigningKeyCache, IOptions<JwtValidationOptions>>((bearer, keyCache, opts) =>
            {
                var o = opts.Value;
                bearer.MapInboundClaims = false; // keep "sub"/"role" claim names verbatim
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = o.Issuer,
                    ValidateAudience = true,
                    ValidAudience = o.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeyResolver = (_, _, kid, _) => keyCache.GetKeys(kid),
                    RoleClaimType = "role",
                    NameClaimType = "sub",
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();
    }

    /// <summary>Applies migrations and seeds prompt templates. Wraps the DataAccess
    /// initializer so the Api can trigger it without referencing EF types.</summary>
    public static Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default) =>
        DatabaseInitializer.InitializeAsync(services, ct);
}

/// <summary>deals-service connection settings, bound from the "Deals" section.</summary>
public class DealsOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5200";
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>search-service connection settings, bound from the "Search" section.</summary>
public class SearchOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5600";
    public int TimeoutSeconds { get; set; } = 15;
}
