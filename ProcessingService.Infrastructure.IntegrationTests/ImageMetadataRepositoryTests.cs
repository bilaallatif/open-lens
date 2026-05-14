using MongoDB.Driver;
using ProcessingService.Domain.Entities;
using ProcessingService.Infrastructure.Data;
using Shared.Repository.Infrastructure.Documents;
using Testcontainers.MongoDb;

namespace ProcessingService.Infrastructure.IntegrationTests;

public class ImageMetadataRepositoryTests : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0").Build();
    private IMongoCollection<ImageMetadataDocument> _collection = null!;
    private ImageMetadataRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var database = new MongoClient(_container.GetConnectionString()).GetDatabase("test");

        _collection = database.GetCollection<ImageMetadataDocument>("image_metadata");
        _repository = new ImageMetadataRepository(database);
    }

    public Task DisposeAsync()
    {
        return _container.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task CreateAndGetById_WithFullExifData_RoundTripsAllFields()
    {
        var entity = new ImageMetadata
        {
            Id = Guid.NewGuid(),
            BlobName = "photos/canon-r5.jpg",
            CreatedAt = new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.Zero),
            ExifData = new ExifData
            {
                CameraMake = "Canon",
                CameraModel = "EOS R5",
                LensMake = "Canon",
                LensModel = "RF 85mm f/1.2L",
                Iso = 400,
                FocalLength = "85 mm",
                FNumber = "f/7.1",
                TakenAt = new DateTimeOffset(2024, 3, 15, 9, 0, 0, TimeSpan.Zero),
                Latitude = 51.5074,
                Longitude = -0.1278,
            },
        };

        await _repository.CreateAsync(entity);
        var result = await _collection.Find(x => x.Id == entity.Id).FirstOrDefaultAsync();

        Assert.NotNull(result);
        Assert.Equal(entity.Id, result.Id);
        Assert.Equal(entity.BlobName, result.BlobName);
        Assert.Equal(entity.CreatedAt, result.CreatedAt);
        Assert.Equal(entity.ExifData.CameraMake, result.ExifData.CameraMake);
        Assert.Equal(entity.ExifData.CameraModel, result.ExifData.CameraModel);
        Assert.Equal(entity.ExifData.LensMake, result.ExifData.LensMake);
        Assert.Equal(entity.ExifData.LensModel, result.ExifData.LensModel);
        Assert.Equal(entity.ExifData.Iso, result.ExifData.Iso);
        Assert.Equal(entity.ExifData.FocalLength, result.ExifData.FocalLength);
        Assert.Equal(entity.ExifData.FNumber, result.ExifData.FNumber);
        Assert.Equal(entity.ExifData.TakenAt, result.ExifData.TakenAt);
        Assert.Equal(entity.ExifData.Latitude, result.ExifData.Latitude);
        Assert.Equal(entity.ExifData.Longitude, result.ExifData.Longitude);
    }

    [Fact]
    public async Task CreateAndGetById_WithNullExifFields_RoundTripsNulls()
    {
        var entity = new ImageMetadata
        {
            Id = Guid.NewGuid(),
            BlobName = "photos/no-exif.jpg",
            CreatedAt = new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.Zero),
            ExifData = new ExifData(),
        };

        await _repository.CreateAsync(entity);
        var result = await _collection.Find(x => x.Id == entity.Id).FirstOrDefaultAsync();

        Assert.NotNull(result);
        Assert.Equal(entity.Id, result.Id);
        Assert.Equal(entity.BlobName, result.BlobName);
        Assert.Null(result.ExifData.CameraMake);
        Assert.Null(result.ExifData.CameraModel);
        Assert.Null(result.ExifData.LensMake);
        Assert.Null(result.ExifData.LensModel);
        Assert.Null(result.ExifData.Iso);
        Assert.Null(result.ExifData.FocalLength);
        Assert.Null(result.ExifData.FNumber);
        Assert.Null(result.ExifData.TakenAt);
        Assert.Null(result.ExifData.Latitude);
        Assert.Null(result.ExifData.Longitude);
    }
}
