using {{Svc}}Service.DataAccess;
using Microsoft.Extensions.DependencyInjection;

namespace {{Svc}}Service.Business;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusiness(this IServiceCollection services, string connectionString)
    {
        // Chain downward: Business owns the DataAccess registration so the Api
        // only ever calls AddBusiness and stays ignorant of the data layer.
        services.AddDataAccess(connectionString);

        // Register one service per aggregate, e.g.:
        // services.AddScoped<{{Aggregate}}Service>();

        return services;
    }
}
