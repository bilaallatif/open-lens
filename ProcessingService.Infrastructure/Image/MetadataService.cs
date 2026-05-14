using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using ProcessingService.Application.Interfaces;
using ProcessingService.Domain.Entities;

namespace ProcessingService.Infrastructure.Image;

public class MetadataService : IMetadataService
{
    public ExifData GetImageMetadata(Stream stream)
    {
        var directories = ImageMetadataReader.ReadMetadata(stream);

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        var location = gps?.GetGeoLocation();

        return new ExifData
        {
            CameraMake = ifd0?.GetDescription(ExifDirectoryBase.TagMake),
            CameraModel = ifd0?.GetDescription(ExifDirectoryBase.TagModel),
            LensMake = ifd0?.GetDescription(ExifDirectoryBase.TagLensMake),
            LensModel = ifd0?.GetDescription(ExifDirectoryBase.TagLensModel),
            Iso = int.TryParse(
                subIfd?.GetDescription(ExifDirectoryBase.TagIsoEquivalent),
                out var result
            )
                ? result
                : null,
            FocalLength = subIfd?.GetDescription(ExifDirectoryBase.TagFocalLength),
            FNumber = subIfd?.GetDescription(ExifDirectoryBase.TagFNumber),
            TakenAt = DateTimeOffset.TryParseExact(
                subIfd?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal),
                "yyyy:MM:dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var offset
            )
                ? offset
                : null,
            Latitude = location?.Latitude,
            Longitude = location?.Longitude,
        };
    }
}
