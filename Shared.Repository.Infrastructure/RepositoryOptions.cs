using System.ComponentModel.DataAnnotations;

namespace Shared.Repository.Infrastructure;

public class RepositoryOptions
{
    [Required]
    public string ConnectionString { get; set; } = null!;

    [Required]
    public string DatabaseName { get; set; } = null!;
}
