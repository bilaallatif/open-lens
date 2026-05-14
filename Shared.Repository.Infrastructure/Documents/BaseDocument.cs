using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Repository.Infrastructure.Documents;

public class BaseDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
