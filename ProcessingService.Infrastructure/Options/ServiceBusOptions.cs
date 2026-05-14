namespace ProcessingService.Infrastructure.Options;

public class ServiceBusOptions
{
    public required string ConnectionString { get; init; }
    public required string QueueName { get; init; }
}
