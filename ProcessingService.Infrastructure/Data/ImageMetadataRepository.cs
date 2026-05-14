using MongoDB.Driver;
using ProcessingService.Application.Interfaces;
using ProcessingService.Domain.Entities;
using Shared.Repository.Infrastructure.Documents;

namespace ProcessingService.Infrastructure.Data;

public class ImageMetadataRepository : IMetadataRepository
{
    private readonly IMongoCollection<ImageMetadataDocument> _collection;

    public ImageMetadataRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ImageMetadataDocument>("image_metadata");
    }

    public async Task CreateAsync(ImageMetadata item)
    {
        await _collection.InsertOneAsync(ToDocument(item));
    }

    private ImageMetadataDocument ToDocument(ImageMetadata entity)
    {
        return new ImageMetadataDocument
        {
            Id = entity.Id,
            CreatedAt = entity.CreatedAt,
            BlobName = entity.BlobName,
            ExifData = new ExifDataDocument
            {
                CameraMake = entity.ExifData.CameraMake,
                CameraModel = entity.ExifData.CameraModel,
                LensMake = entity.ExifData.LensMake,
                LensModel = entity.ExifData.LensModel,
                Iso = entity.ExifData.Iso,
                FocalLength = entity.ExifData.FocalLength,
                FNumber = entity.ExifData.FNumber,
                TakenAt = entity.ExifData.TakenAt,
                Latitude = entity.ExifData.Latitude,
                Longitude = entity.ExifData.Longitude,
            },
        };
    }
}
