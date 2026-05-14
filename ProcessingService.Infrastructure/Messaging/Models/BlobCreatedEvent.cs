namespace ProcessingService.Infrastructure.Messaging.Models;

// Azure Event Grid schema for Microsoft.Storage.BlobCreated
// Subject format: /blobServices/default/containers/{container}/blobs/{blobName}
public record BlobCreatedEvent(string Subject);
