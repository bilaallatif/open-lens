using ProcessingService.Application.Interfaces;

namespace ProcessingService.Worker.Workers;

public class ImageProcessingWorker : BackgroundService
{
    private readonly IImageQueueListener _listener;
    private readonly ILogger<ImageProcessingWorker> _logger;

    public ImageProcessingWorker(
        IImageQueueListener listener,
        ILogger<ImageProcessingWorker> logger
    )
    {
        _listener = listener;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ImageProcessingWorker starting");
        await _listener.StartAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _listener.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
