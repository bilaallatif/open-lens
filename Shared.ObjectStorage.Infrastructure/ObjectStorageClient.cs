using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Shared.ObjectStorage.Application;

namespace Shared.ObjectStorage.Infrastructure;

public class ObjectStorageClient(BlobContainerClient blobContainerClient) : IObjectStorageClient
{
    public async Task<Stream> GetObjectStreamAsync(string blobName)
    {
        var blobClient = blobContainerClient.GetBlobClient(blobName);
        return await blobClient.OpenReadAsync();
    }

    public Uri GetObjectUploadUri(string blobName)
    {
        var blobClient = blobContainerClient.GetBlobClient(blobName);

        var sasBuilder = new BlobSasBuilder(
            BlobSasPermissions.Write | BlobSasPermissions.Create,
            DateTimeOffset.UtcNow.AddMinutes(15)
        )
        {
            BlobContainerName = blobContainerClient.Name,
            BlobName = blobName,
        };

        return blobClient.GenerateSasUri(sasBuilder);
    }
}
