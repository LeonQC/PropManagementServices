using ListingsService.DataAccess;
using Microsoft.Extensions.DependencyInjection;

namespace ListingsService.Business;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusiness(this IServiceCollection services, string connectionString)
    {
        services.AddDataAccess(connectionString);
        services.AddScoped<PropertyService>();
        return services;
    }
}
