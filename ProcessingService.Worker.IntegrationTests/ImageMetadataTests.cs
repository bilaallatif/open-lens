using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ProcessingService.Infrastructure.Options;
using Shared.Repository.Infrastructure.Documents;
using Testcontainers.Azurite;
using Testcontainers.MongoDb;
using Testcontainers.ServiceBus;

namespace ProcessingService.Worker.IntegrationTests;

public class ImageMetadataTests : IAsyncLifetime
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder(
        "mcr.microsoft.com/azure-storage/azurite:latest"
    ).Build();

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8.0").Build();

    private readonly ServiceBusContainer _serviceBus = new ServiceBusBuilder(
        "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest"
    )
        .WithAcceptLicenseAgreement(true)
        .WithConfig("Config.json")
        .Build();

    private IMongoCollection<ImageMetadataDocument> _collection = null!;

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _azurite.StartAsync();
        await _mongo.StartAsync();
        await _serviceBus.StartAsync();

        var database = new MongoClient(_mongo.GetConnectionString()).GetDatabase("open-lens");
        _collection = database.GetCollection<ImageMetadataDocument>("image_metadata");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ObjectStorage:ConnectionString", _azurite.GetConnectionString());
            host.UseSetting("ObjectStorage:ContainerName", "images");
            host.UseSetting("Repository:ConnectionString", _mongo.GetConnectionString());
            host.UseSetting("Repository:DatabaseName", "open-lens");
            host.UseSetting("ServiceBus:ConnectionString", _serviceBus.GetConnectionString());
            host.UseSetting("ServiceBus:QueueName", "image-uploaded");
        });

        _ = _factory.Server; // starts the host and background services
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _serviceBus.DisposeAsync();
        await _mongo.StopAsync();
        await _azurite.StopAsync();
    }

    [Fact]
    public async Task UploadedImagePersistImageMetadata()
    {
        // Upload pre-existing blob
        const string blobName = "test.jpg";

        var blobContainerClient = _factory.Services.GetRequiredService<BlobContainerClient>();
        await blobContainerClient.CreateIfNotExistsAsync();
        var stream = File.OpenRead(Path.Combine("Assets", "test.jpg"));
        await blobContainerClient.UploadBlobAsync(blobName, stream);

        // Enqueue 'BlobCreated' event
        var serviceBusClient = new ServiceBusClient(
            _factory
                .Services.GetRequiredService<IOptions<ServiceBusOptions>>()
                .Value.ConnectionString
        );
        var sender = serviceBusClient.CreateSender(
            _factory.Services.GetRequiredService<IOptions<ServiceBusOptions>>().Value.QueueName
        );

        var subject = $"/blobServices/default/containers/images/blobs/{blobName}";
        var body = BinaryData.FromString($$"""{"Subject":"{{subject}}"}""");
        await sender.SendMessageAsync(new ServiceBusMessage(body));

        // Poll until the worker persists the metadata
        ImageMetadataDocument? record = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!cts.IsCancellationRequested)
        {
            record = await _collection.Find(x => x.BlobName == blobName).FirstOrDefaultAsync();
            if (record is not null)
                break;
            await Task.Delay(500, cts.Token)
                .ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing
                        | ConfigureAwaitOptions.ContinueOnCapturedContext
                );
        }

        Assert.NotNull(record);
        Assert.Equal(blobName, record.BlobName);
        Assert.NotNull(record.ExifData);
    }
}
