namespace SearchService.Domain.Common;

public class BaseEntity
{
    public Guid Id { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
