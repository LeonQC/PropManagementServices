using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PropTrack.Messaging;
using SearchService.Business.Consumers;
using SearchService.Business.Security;
using SearchService.DataAccess;

namespace SearchService.Business;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusiness(this IServiceCollection services, IConfiguration config)
    {
        services.AddDataAccess(config);
        services.AddKafkaMessaging(config);

        services.AddScoped<PropertyIndexingService>();
        services.AddScoped<PropertySearchService>();
        services.AddScoped<DealIndexingService>();
        services.AddScoped<DealSearchService>();

        // Inbound event consumers (background services), one per topic and per consumer group.
        services.AddHostedService<PropertySnapshotConsumer>();
        services.AddHostedService<DealSnapshotConsumer>();

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
    /// Only the deals endpoints are gated. Properties stay anonymous, matching
    /// listings-service — the source of the data this service mirrors is what decides,
    /// not the fact that both live behind one process.
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
}
