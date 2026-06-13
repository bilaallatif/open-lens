var builder = DistributedApplication.CreateBuilder(args);

// Azure Blob Storage (Azurite emulator) — stores uploaded images.
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();
var blobs = storage.AddBlobs("blobs");

// Azure Service Bus (emulator) — "image-uploaded" queue drives processing.
var serviceBus = builder.AddAzureServiceBus("servicebus")
    .RunAsEmulator();
var imageUploadedQueue = serviceBus.AddServiceBusQueue("image-uploaded");

// MongoDB — image metadata store, persisted across runs via a data volume.
var mongo = builder.AddMongoDB("mongo")
    .WithDataVolume();
var imageMetadataDb = mongo.AddDatabase("open-lens");

// UploadService.Api — receives uploads and writes blobs.
builder.AddProject<Projects.UploadService_Api>("uploadservice")
    .WaitFor(storage)
    .WithEnvironment("ObjectStorage__ConnectionString", blobs);

// ProcessingService.Worker — consumes the queue, reads blobs, writes metadata.
builder.AddProject<Projects.ProcessingService_Worker>("processingservice")
    .WaitFor(imageUploadedQueue)
    .WaitFor(storage)
    .WaitFor(imageMetadataDb)
    .WithEnvironment("ServiceBus__ConnectionString", serviceBus)
    .WithEnvironment("ObjectStorage__ConnectionString", blobs)
    .WithEnvironment("Repository__ConnectionString", imageMetadataDb);

// SearchService.Api — queries image metadata.
builder.AddProject<Projects.SearchService_Api>("searchservice")
    .WaitFor(imageMetadataDb)
    .WithEnvironment("Repository__ConnectionString", imageMetadataDb);

builder.Build().Run();
