using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using MongoDB.Driver;
using SearchService.Application.Common;
using SearchService.Domain.Entities;
using Shared.Repository.Infrastructure.Documents;
using Testcontainers.MongoDb;

namespace SearchService.Api.IntegrationTests;

public class ImageMetadataTests : IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8.0").Build();

    private readonly ImageMetadataDocument _seedDocument = new()
    {
        Id = Guid.NewGuid(),
        BlobName = "test.jpg",
        CreatedAt = new DateTimeOffset(2025, 1, 20, 9, 30, 0, TimeSpan.Zero),
        ExifData = new ExifDataDocument
        {
            CameraMake = "Canon",
            CameraModel = "EOS R5",
            LensMake = "Canon",
            LensModel = "RF 24-70 f/2.8L",
            Iso = 100,
            TakenAt = new DateTimeOffset(2025, 1, 15, 9, 0, 0, TimeSpan.Zero),
        },
    };

    private HttpClient _client = null!;
    private IMongoCollection<ImageMetadataDocument> _collection = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _mongo.StartAsync();

        var database = new MongoClient(_mongo.GetConnectionString()).GetDatabase("open-lens");
        _collection = database.GetCollection<ImageMetadataDocument>("image_metadata");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Repository:ConnectionString", _mongo.GetConnectionString());
            host.UseSetting("Repository:DatabaseName", "open-lens");
        });
        _client = _factory.CreateClient();

        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _mongo.DisposeAsync();
    }

    private Task SeedAsync()
    {
        return _collection.InsertOneAsync(_seedDocument);
    }

    [Fact]
    public async Task GetImageMetadataMatchingCriteria_WithSeededData_ReturnsSeededData()
    {
        // Act
        var url = QueryHelpers.AddQueryString(
            "/image-metadata",
            new Dictionary<string, string?> { ["cameraMake"] = "Canon" }
        );
        var response = await _client.GetAsync(url);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ImageMetadata>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equivalent(_seedDocument, result.Items[0], true);
    }

    [Fact]
    public async Task GetImageMetadataMisMatchingCriteria_WithSeededData_ReturnsEmpty()
    {
        // Act
        var url = QueryHelpers.AddQueryString(
            "/image-metadata",
            new Dictionary<string, string?> { ["cameraMake"] = "N/A" }
        );
        var response = await _client.GetAsync(url);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ImageMetadata>>();
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Empty(result.Items);
    }
}
