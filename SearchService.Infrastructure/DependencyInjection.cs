using Microsoft.Extensions.DependencyInjection;
using SearchService.Application.Interfaces;
using Shared.Repository.Infrastructure;

namespace SearchService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // Repository registration
        services.AddRepositoryServices();
        services.AddScoped<IMetadataRepository, ImageMetadataRepository>();

        return services;
    }
}
