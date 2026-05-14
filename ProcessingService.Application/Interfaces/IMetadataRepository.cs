namespace ProcessingService.Application.Interfaces;

public interface IMetadataRepository
{
    Task CreateAsync(Domain.Entities.ImageMetadata item);
}
