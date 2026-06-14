using ProcessingService.Infrastructure.Image;

namespace ProcessingService.Infrastructure.IntegrationTests;

public class MetadataServiceTests
{
    [Fact]
    public void ScrapeMetadataTest()
    {
        var metadataService = new MetadataService();
        var stream = File.OpenRead(Path.Combine("Assets", "test.jpg"));

        var metadata = metadataService.GetImageMetadata(stream);

        Assert.NotNull(metadata);
        Assert.Equal("Canon", metadata.CameraMake);
        Assert.Equal("Canon EOS 40D", metadata.CameraModel);
        Assert.Null(metadata.LensMake);
        Assert.Null(metadata.LensModel);
        Assert.Equal(100, metadata.Iso);
        Assert.Equal("135 mm", metadata.FocalLength);
        Assert.Equal("f/7.1", metadata.FNumber);
        Assert.Equal(new DateTimeOffset(2008, 5, 30, 15, 56, 1, TimeSpan.Zero), metadata.TakenAt);
        Assert.Null(metadata.Latitude);
        Assert.Null(metadata.Longitude);
    }
}
