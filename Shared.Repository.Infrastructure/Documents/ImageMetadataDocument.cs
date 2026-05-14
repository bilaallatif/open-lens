namespace Shared.Repository.Infrastructure.Documents;

public class ImageMetadataDocument : BaseDocument
{
    public required string BlobName { get; init; }
    public required ExifDataDocument ExifData { get; init; }
}
