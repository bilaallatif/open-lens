namespace Shared.Repository.Infrastructure.Documents;

public class ExifDataDocument
{
    // Equipment
    public string? CameraMake { get; init; }
    public string? CameraModel { get; init; }

    public string? LensMake { get; init; }
    public string? LensModel { get; init; }

    // Exposure
    public int? Iso { get; init; }
    public string? FocalLength { get; init; }
    public string? FNumber { get; init; }

    // Shot details
    public DateTimeOffset? TakenAt { get; init; }

    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}
