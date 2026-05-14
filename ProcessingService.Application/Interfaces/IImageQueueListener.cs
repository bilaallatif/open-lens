namespace ProcessingService.Application.Interfaces;

public interface IImageQueueListener
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
