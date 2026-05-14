using MongoDB.Driver;
using SearchService.Application.Common;
using Shared.Repository.Infrastructure.Documents;
using Testcontainers.MongoDb;

namespace SearchService.Infrastructure.IntegrationTests;

public class ImageMetadataRepositoryTests : IAsyncLifetime
{
    private static readonly Guid CanonR5Jan = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CanonR5Jun = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CanonR6Mar = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid NikonZ8Feb = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid NikonZ8Jul = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid SonyA7May = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid SonyA7Aug = new("77777777-7777-7777-7777-777777777777");
    private static readonly Guid NullExif = new("88888888-8888-8888-8888-888888888888");

    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0").Build();
    private IMongoCollection<ImageMetadataDocument> _collection = null!;
    private ImageMetadataRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var database = new MongoClient(_container.GetConnectionString()).GetDatabase("test");

        _collection = database.GetCollection<ImageMetadataDocument>("image_metadata");
        _repository = new ImageMetadataRepository(database);

        await SeedAsync();
    }

    public Task DisposeAsync()
    {
        return _container.DisposeAsync().AsTask();
    }

    public static IEnumerable<object[]> FilterCases()
    {
        return
        [
            [
                "no criteria returns all seeded documents",
                new ImageSearchCriteria(),
                new[]
                {
                    CanonR5Jan,
                    CanonR5Jun,
                    CanonR6Mar,
                    NikonZ8Feb,
                    NikonZ8Jul,
                    SonyA7May,
                    SonyA7Aug,
                    NullExif,
                },
            ],
            [
                "CameraMake matches across models, excludes null EXIF",
                new ImageSearchCriteria("Canon"),
                new[] { CanonR5Jan, CanonR5Jun, CanonR6Mar },
            ],
            [
                "CameraModel narrows to a single body",
                new ImageSearchCriteria(CameraModel: "EOS R5"),
                new[] { CanonR5Jan, CanonR5Jun },
            ],
            [
                "LensMake and LensModel combine",
                new ImageSearchCriteria(LensMake: "Sony", LensModel: "GM 35 f/1.4"),
                new[] { SonyA7May, SonyA7Aug },
            ],
            [
                "IsoMin includes only ISO >= bound, excludes null ISO",
                new ImageSearchCriteria(IsoMin: 1000),
                new[] { CanonR5Jun, NikonZ8Jul, SonyA7Aug },
            ],
            [
                "IsoMin and IsoMax compose a range",
                new ImageSearchCriteria(IsoMin: 200, IsoMax: 2000),
                new[] { CanonR6Mar, SonyA7May, SonyA7Aug },
            ],
            [
                "TakenAfter excludes earlier dates and null TakenAt",
                new ImageSearchCriteria(
                    TakenAfter: new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)
                ),
                new[] { CanonR5Jun, NikonZ8Jul, SonyA7Aug },
            ],
            [
                "TakenBefore excludes later dates and null TakenAt",
                new ImageSearchCriteria(
                    TakenBefore: new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero)
                ),
                new[] { CanonR5Jan, NikonZ8Feb },
            ],
            [
                "TakenAfter and TakenBefore compose a window",
                new ImageSearchCriteria(
                    TakenAfter: new DateTimeOffset(2025, 4, 1, 0, 0, 0, TimeSpan.Zero),
                    TakenBefore: new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero)
                ),
                new[] { SonyA7May, CanonR5Jun },
            ],
            [
                "CameraMake combined with IsoMax narrows further",
                new ImageSearchCriteria("Canon", IsoMax: 200),
                new[] { CanonR5Jan },
            ],
            [
                "CameraMake combined with IsoMin narrows further",
                new ImageSearchCriteria("Canon", IsoMin: 200),
                new[] { CanonR5Jun, CanonR6Mar },
            ],
            [
                "Unmatched filter returns empty set",
                new ImageSearchCriteria(LensModel: "does-not-exist"),
                Array.Empty<Guid>(),
            ],
        ];
    }

    private Task SeedAsync()
    {
        var documents = new[]
        {
            Document(
                CanonR5Jan,
                "Canon",
                "EOS R5",
                "Canon",
                "RF 24-70 f/2.8L",
                100,
                new DateTimeOffset(2025, 1, 15, 9, 0, 0, TimeSpan.Zero)
            ),
            Document(
                CanonR5Jun,
                "Canon",
                "EOS R5",
                "Canon",
                "RF 24-70 f/2.8L",
                6400,
                new DateTimeOffset(2025, 6, 20, 9, 0, 0, TimeSpan.Zero)
            ),
            Document(
                CanonR6Mar,
                "Canon",
                "EOS R6",
                "Canon",
                "RF 50 f/1.2L",
                400,
                new DateTimeOffset(2025, 3, 10, 9, 0, 0, TimeSpan.Zero)
            ),
            Document(
                NikonZ8Feb,
                "Nikon",
                "Z8",
                "Nikon",
                "Nikkor Z 24-70 f/2.8 S",
                100,
                new DateTimeOffset(2025, 2, 5, 9, 0, 0, TimeSpan.Zero)
            ),
            Document(
                NikonZ8Jul,
                "Nikon",
                "Z8",
                "Nikon",
                "Nikkor Z 70-200 f/2.8 S",
                3200,
                new DateTimeOffset(2025, 7, 12, 9, 0, 0, TimeSpan.Zero)
            ),
            Document(
                SonyA7May,
                "Sony",
                "A7IV",
                "Sony",
                "GM 35 f/1.4",
                800,
                new DateTimeOffset(2025, 5, 22, 9, 0, 0, TimeSpan.Zero)
            ),
            Document(
                SonyA7Aug,
                "Sony",
                "A7IV",
                "Sony",
                "GM 35 f/1.4",
                1600,
                new DateTimeOffset(2025, 8, 30, 9, 0, 0, TimeSpan.Zero)
            ),
            new ImageMetadataDocument
            {
                Id = NullExif,
                BlobName = "no-exif.jpg",
                CreatedAt = DateTimeOffset.UtcNow,
                ExifData = new ExifDataDocument(),
            },
        };

        return _collection.InsertManyAsync(documents);
    }

    private static ImageMetadataDocument Document(
        Guid id,
        string cameraMake,
        string cameraModel,
        string lensMake,
        string lensModel,
        int iso,
        DateTimeOffset takenAt
    )
    {
        return new ImageMetadataDocument
        {
            Id = id,
            BlobName = $"{id}.jpg",
            CreatedAt = DateTimeOffset.UtcNow,
            ExifData = new ExifDataDocument
            {
                CameraMake = cameraMake,
                CameraModel = cameraModel,
                LensMake = lensMake,
                LensModel = lensModel,
                Iso = iso,
                TakenAt = takenAt,
            },
        };
    }

    [Theory]
    [MemberData(nameof(FilterCases))]
    public async Task SearchAsync_WithCriteria_ReturnsMatchingDocuments(
        string scenario,
        ImageSearchCriteria criteria,
        Guid[] expectedIds
    )
    {
        _ = scenario;

        var result = await _repository.SearchAsync(criteria, 1, 100, CancellationToken.None);

        var actualIds = result.Items.Select(i => i.Id).OrderBy(g => g);
        var sortedExpected = expectedIds.OrderBy(g => g);

        Assert.Equal(sortedExpected, actualIds);
        Assert.Equal(expectedIds.Length, result.TotalCount);
    }
}
