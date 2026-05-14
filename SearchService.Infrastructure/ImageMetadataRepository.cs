using MongoDB.Driver;
using SearchService.Application.Common;
using SearchService.Application.Interfaces;
using SearchService.Domain.Entities;
using Shared.Repository.Infrastructure.Documents;

namespace SearchService.Infrastructure;

public class ImageMetadataRepository : IMetadataRepository
{
    private const string CollectionName = "image_metadata";

    private static readonly SortDefinition<ImageMetadataDocument> DefaultSort =
        Builders<ImageMetadataDocument>
            .Sort.Descending(x => x.ExifData.TakenAt)
            .Descending(x => x.CreatedAt);

    private readonly IMongoCollection<ImageMetadataDocument> _collection;

    public ImageMetadataRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ImageMetadataDocument>(CollectionName);
    }

    public async Task<PagedResult<ImageMetadata>> SearchAsync(
        ImageSearchCriteria criteria,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var filter = BuildFilter(criteria);

        var totalCount = await _collection.CountDocumentsAsync(
            filter,
            cancellationToken: cancellationToken
        );

        var documents = await _collection
            .Find(filter)
            .Sort(DefaultSort)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        var items = documents.Select(ToDomain).ToList();
        return new PagedResult<ImageMetadata>(items, page, pageSize, totalCount);
    }

    private static FilterDefinition<ImageMetadataDocument> BuildFilter(ImageSearchCriteria criteria)
    {
        var builder = Builders<ImageMetadataDocument>.Filter;
        var filters = new List<FilterDefinition<ImageMetadataDocument>>();

        if (criteria.CameraMake is not null)
            filters.Add(builder.Eq(x => x.ExifData.CameraMake, criteria.CameraMake));

        if (criteria.CameraModel is not null)
            filters.Add(builder.Eq(x => x.ExifData.CameraModel, criteria.CameraModel));

        if (criteria.LensMake is not null)
            filters.Add(builder.Eq(x => x.ExifData.LensMake, criteria.LensMake));

        if (criteria.LensModel is not null)
            filters.Add(builder.Eq(x => x.ExifData.LensModel, criteria.LensModel));

        if (criteria.IsoMin is not null)
            filters.Add(builder.Gte(x => x.ExifData.Iso, criteria.IsoMin));

        if (criteria.IsoMax is not null)
            filters.Add(builder.Lte(x => x.ExifData.Iso, criteria.IsoMax));

        if (criteria.TakenAfter is not null)
            filters.Add(builder.Gte(x => x.ExifData.TakenAt, criteria.TakenAfter));

        if (criteria.TakenBefore is not null)
            filters.Add(builder.Lte(x => x.ExifData.TakenAt, criteria.TakenBefore));

        return filters.Count == 0 ? builder.Empty : builder.And(filters);
    }

    private static ImageMetadata ToDomain(ImageMetadataDocument document)
    {
        return new ImageMetadata
        {
            Id = document.Id,
            CreatedAt = document.CreatedAt,
            BlobName = document.BlobName,
            ExifData = new ExifData
            {
                CameraMake = document.ExifData.CameraMake,
                CameraModel = document.ExifData.CameraModel,
                LensMake = document.ExifData.LensMake,
                LensModel = document.ExifData.LensModel,
                Iso = document.ExifData.Iso,
                FocalLength = document.ExifData.FocalLength,
                FNumber = document.ExifData.FNumber,
                TakenAt = document.ExifData.TakenAt,
                Latitude = document.ExifData.Latitude,
                Longitude = document.ExifData.Longitude,
            },
        };
    }
}
