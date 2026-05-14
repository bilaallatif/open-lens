using Microsoft.Extensions.DependencyInjection;
using ProcessingService.Application.Interfaces;
using ProcessingService.Infrastructure.Data;
using ProcessingService.Infrastructure.Image;
using ProcessingService.Infrastructure.Messaging;
using ProcessingService.Infrastructure.Options;
using Shared.ObjectStorage.Infrastructure;
using Shared.Repository.Infrastructure;

namespace ProcessingService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // Repository registration
        services.AddRepositoryServices();
        services.AddScoped<IMetadataRepository, ImageMetadataRepository>();

        // Metadata scraping service
        services.AddSingleton<IMetadataService, MetadataService>();

        // Service Bus registration
        services.AddOptions<ServiceBusOptions>().BindConfiguration("ServiceBus");

        services.AddSingleton<IImageQueueListener, ServiceBusImageQueueListener>();

        // Object storage registration
        services.AddObjectStorageServices();

        return services;
    }
}
