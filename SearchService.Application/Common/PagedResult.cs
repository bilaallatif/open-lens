namespace SearchService.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);
