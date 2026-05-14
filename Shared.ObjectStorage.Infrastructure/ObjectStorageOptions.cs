using System.ComponentModel.DataAnnotations;

namespace Shared.ObjectStorage.Infrastructure;

public class ObjectStorageOptions
{
    [Required]
    public string ConnectionString { get; init; } = null!;

    [Required]
    public string ContainerName { get; init; } = null!;
}
