using SearchService.Domain.Common;

namespace SearchService.Domain.Entities;

public class ImageMetadata : BaseEntity
{
    public required string BlobName { get; init; }
    public required ExifData ExifData { get; init; }
}
