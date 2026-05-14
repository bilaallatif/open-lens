using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProcessingService.Application.ImageMetadata.Commands.CreateImageMetadata;
using ProcessingService.Application.Interfaces;
using ProcessingService.Infrastructure.Messaging.Models;
using ProcessingService.Infrastructure.Options;

namespace ProcessingService.Infrastructure.Messaging;

public class ServiceBusImageQueueListener : IImageQueueListener
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<ServiceBusImageQueueListener> _logger;
    private readonly ServiceBusOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private ServiceBusProcessor? _processor;

    public ServiceBusImageQueueListener(
        ServiceBusClient client,
        IOptions<ServiceBusOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<ServiceBusImageQueueListener> logger
    )
    {
        _client = client;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _processor = _client.CreateProcessor(
            _options.QueueName,
            new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 1 }
        );

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        _logger.LogInformation(
            "Starting Service Bus processor for queue {QueueName}",
            _options.QueueName
        );
        await _processor.StartProcessingAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var blobCreatedEvent = args.Message.Body.ToObjectFromJson<BlobCreatedEvent>();
            if (blobCreatedEvent is null)
            {
                _logger.LogWarning(
                    "Discarding message {MessageId}: body could not be deserialized to a BlobCreatedEvent",
                    args.Message.MessageId
                );
                await args.DeadLetterMessageAsync(args.Message);
                return;
            }

            var command = ToCreateImageMetadataCommand(blobCreatedEvent);

            _logger.LogInformation(
                "Received BlobCreated event (MessageId={MessageId}): {BlobName}",
                args.Message.MessageId,
                command.BlobName
            );

            using var scope = _scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(command);

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message {MessageId}", args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private static CreateImageMetadataCommand ToCreateImageMetadataCommand(
        BlobCreatedEvent blobCreatedEvent
    )
    {
        const string containerPrefix = "/blobServices/default/containers/";
        const string blobsSegment = "/blobs/";

        var subject = blobCreatedEvent.Subject;
        var blobsIndex = subject.IndexOf(
            blobsSegment,
            containerPrefix.Length,
            StringComparison.Ordinal
        );
        var blobName = subject[(blobsIndex + blobsSegment.Length)..];

        return new CreateImageMetadataCommand(blobName);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Service Bus error from {ErrorSource} on {EntityPath}",
            args.ErrorSource,
            args.EntityPath
        );
        return Task.CompletedTask;
    }
}
