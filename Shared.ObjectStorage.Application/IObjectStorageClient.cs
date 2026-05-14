namespace Shared.ObjectStorage.Application;

public interface IObjectStorageClient
{
    Task<Stream> GetObjectStreamAsync(string blobName);
    Uri GetObjectUploadUri(string blobName);
}
