using ProcessingService.Domain.Entities;

namespace ProcessingService.Application.Interfaces;

public interface IMetadataService
{
    ExifData GetImageMetadata(Stream stream);
}
