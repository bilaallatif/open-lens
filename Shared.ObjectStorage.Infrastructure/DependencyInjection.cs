using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.ObjectStorage.Application;

namespace Shared.ObjectStorage.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddObjectStorageServices(this IServiceCollection services)
    {
        services
            .AddOptions<ObjectStorageOptions>()
            .BindConfiguration("ObjectStorage")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // TODO: update to use credential based access
        // this will require generation of `UserDelegationKey` in client
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;
            return new BlobContainerClient(
                options.ConnectionString,
                options.ContainerName,
                new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_08_04)
            );
        });

        services.AddSingleton<IObjectStorageClient, ObjectStorageClient>();

        return services;
    }
}
