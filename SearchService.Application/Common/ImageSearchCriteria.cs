namespace SearchService.Application.Common;

public record ImageSearchCriteria(
    string? CameraMake = null,
    string? CameraModel = null,
    string? LensMake = null,
    string? LensModel = null,
    int? IsoMin = null,
    int? IsoMax = null,
    DateTimeOffset? TakenAfter = null,
    DateTimeOffset? TakenBefore = null
);
