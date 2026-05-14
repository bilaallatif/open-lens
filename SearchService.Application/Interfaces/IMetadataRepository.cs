using SearchService.Application.Common;

namespace SearchService.Application.Interfaces;

public interface IMetadataRepository
{
    Task<PagedResult<Domain.Entities.ImageMetadata>> SearchAsync(
        ImageSearchCriteria criteria,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    );
}
