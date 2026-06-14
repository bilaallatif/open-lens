using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;
using MongoDB.Driver;
using SearchService.Application.Common;
using SearchService.Domain.Entities;

namespace AppHost.Tests;

public class EndToEndTests : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private DistributedApplication _app = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        _app = await appHost.BuildAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task GetImageMetadataMatchingCriteria_WithUploadedAndProcessedImage_ReturnsImageMetadata()
    {
        // Arrange
        await _app.StartAsync().WaitAsync(DefaultTimeout);
        await _app
            .ResourceNotifications.WaitForResourceHealthyAsync("uploadservice")
            .WaitAsync(DefaultTimeout);
        await _app
            .ResourceNotifications.WaitForResourceHealthyAsync("processingservice")
            .WaitAsync(DefaultTimeout);
        await _app
            .ResourceNotifications.WaitForResourceHealthyAsync("searchservice")
            .WaitAsync(DefaultTimeout);

        using var uploadClient = _app.CreateHttpClient("uploadservice");
        using var searchClient = _app.CreateHttpClient("searchservice");

        var serviceBusClient = new ServiceBusClient(
            await _app.GetConnectionStringAsync("servicebus")
        );
        var sender = serviceBusClient.CreateSender("image-uploaded");

        // Act
        // Upload image to object store
        using var presignedUrlResponse = await uploadClient.GetAsync("/presigned-url");
        Assert.Equal(HttpStatusCode.OK, presignedUrlResponse.StatusCode);

        var imageBytes = await File.ReadAllBytesAsync(Path.Combine("Assets", "test.jpg"));
        var body = await presignedUrlResponse.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        var uploadUrl = json.GetProperty("uploadUrl").GetString()!;
        var blobName = json.GetProperty("blobName").GetString()!;

        using var blobClient = new HttpClient();
        using var content = new ByteArrayContent(imageBytes);
        content.Headers.Add("x-ms-blob-type", "BlockBlob");
        var uploadResponse = await blobClient.PutAsync(uploadUrl, content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        // Enqueue message to ProcessingService bus
        var subject = $"/blobServices/default/containers/images/blobs/{blobName}";
        var messageBody = BinaryData.FromString($$"""{"Subject":"{{subject}}"}""");
        await sender.SendMessageAsync(new ServiceBusMessage(messageBody));

        // Assert
        PagedResult<ImageMetadata>? result = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!cts.IsCancellationRequested)
        {
            var response = await searchClient.GetAsync("/image-metadata", cts.Token);
            result = await response.Content.ReadFromJsonAsync<PagedResult<ImageMetadata>>(
                cancellationToken: cts.Token
            );
            if (result is not null && result.TotalCount == 1)
                break;

            await Task.Delay(500, cts.Token)
                .ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing
                        | ConfigureAwaitOptions.ContinueOnCapturedContext
                );
        }

        Assert.Equal(blobName, result!.Items[0].BlobName);
    }
}
