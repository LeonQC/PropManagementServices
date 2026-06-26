using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace {{Svc}}Service.DataAccess;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataAccess(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<{{Svc}}DbContext>(options =>
            options.UseNpgsql(connectionString)
                   .UseSnakeCaseNamingConvention());

        // Register one repository per aggregate, interface + implementation:
        // services.AddScoped<I{{Aggregate}}Repository, {{Aggregate}}Repository>();

        return services;
    }
}
