using ProcessingService.Domain.Common;

namespace ProcessingService.Domain.Entities;

public class ImageMetadata : BaseEntity
{
    public required string BlobName { get; init; }
    public required ExifData ExifData { get; init; }
}
