using MediatR;
using SearchService.Application.Common;
using SearchService.Application.ImageMetadata.Queries.GetImageMetadata;

namespace SearchService.Api.Endpoints;

public static class ImageMetadata
{
    public static IEndpointRouteBuilder MapImageMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/image-metadata",
            async (
                ISender sender,
                string? cameraMake,
                string? cameraModel,
                string? lensMake,
                string? lensModel,
                int? isoMin,
                int? isoMax,
                DateTimeOffset? takenAfter,
                DateTimeOffset? takenBefore,
                int page = 1,
                int pageSize = 20
            ) =>
            {
                var criteria = new ImageSearchCriteria(
                    cameraMake,
                    cameraModel,
                    lensMake,
                    lensModel,
                    isoMin,
                    isoMax,
                    takenAfter,
                    takenBefore
                );
                return await sender.Send(new GetImageMetadataQuery(criteria, page, pageSize));
            }
        );

        // TODO: convert to paged result DTO

        // TODO: validation
        // - pageSize >=1
        // - isoMin > isoMax
        // - takenAfter > takenBefore

        return app;
    }
}
