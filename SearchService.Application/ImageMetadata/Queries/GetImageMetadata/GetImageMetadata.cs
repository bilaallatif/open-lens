using MediatR;
using SearchService.Application.Common;
using SearchService.Application.Interfaces;

namespace SearchService.Application.ImageMetadata.Queries.GetImageMetadata;

public record GetImageMetadataQuery(ImageSearchCriteria Criteria, int Page, int PageSize)
    : IRequest<PagedResult<Domain.Entities.ImageMetadata>>;

public class GetImageMetadataQueryHandler(IMetadataRepository metadataRepository)
    : IRequestHandler<GetImageMetadataQuery, PagedResult<Domain.Entities.ImageMetadata>>
{
    public async Task<PagedResult<Domain.Entities.ImageMetadata>> Handle(
        GetImageMetadataQuery request,
        CancellationToken cancellationToken
    )
    {
        return await metadataRepository.SearchAsync(
            request.Criteria,
            request.Page,
            request.PageSize,
            cancellationToken
        );
    }
}
