using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DealsService.DataAccess;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataAccess(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<DealsDbContext>(options =>
            options.UseNpgsql(connectionString)
                   .UseSnakeCaseNamingConvention());

        services.AddScoped<IDealRepository, DealRepository>();
        services.AddScoped<IDealTaskRepository, DealTaskRepository>();
        services.AddScoped<IDealCommentRepository, DealCommentRepository>();
        services.AddScoped<IDealDocumentRepository, DealDocumentRepository>();

        return services;
    }
}
