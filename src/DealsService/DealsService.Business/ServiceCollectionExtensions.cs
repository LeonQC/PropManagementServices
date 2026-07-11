using DealsService.Business.Security;
using DealsService.DataAccess;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PropTrack.Messaging;

namespace DealsService.Business;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusiness(this IServiceCollection services, IConfiguration config)
    {
        services.AddDataAccess(config.GetConnectionString("DealsDb")!);
        services.AddKafkaMessaging(config);

        services.AddScoped<DealService>();
        services.AddScoped<DealTaskService>();
        services.AddScoped<DealCommentService>();
        services.AddScoped<DealDocumentService>();

        services.Configure<JwtValidationOptions>(config.GetSection("Jwt"));
        AddJwtBearerAuth(services);

        return services;
    }

    /// <summary>
    /// Registers JWT bearer validation against auth-service-issued RS256 tokens.
    /// Validation parameters mirror the auth-service's own, except the signing key
    /// is resolved from its JWKS endpoint via <see cref="JwksSigningKeyCache"/>
    /// (auth publishes raw JWKS, not an OIDC discovery document).
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
