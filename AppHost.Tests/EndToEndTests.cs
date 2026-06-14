using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;
using SearchService.Application.Common;
using SearchService.Domain.Entities;

namespace AppHost.Tests;

public class EndToEndTests : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private DistributedApplication _app = null!;
    private HttpClient _uploadClient = null!;
    private HttpClient _searchClient = null!;
    private ServiceBusClient _serviceBusClient = null!;
    private ServiceBusSender _processingServiceSender = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        _app = await appHost.BuildAsync();

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

        _uploadClient = _app.CreateHttpClient("uploadservice");
        _searchClient = _app.CreateHttpClient("searchservice");

        _serviceBusClient = new ServiceBusClient(await _app.GetConnectionStringAsync("servicebus"));
        _processingServiceSender = _serviceBusClient.CreateSender("image-uploaded");
    }

    public async Task DisposeAsync()
    {
        _uploadClient.Dispose();
        _searchClient.Dispose();
        await _processingServiceSender.DisposeAsync();
        await _serviceBusClient.DisposeAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task GetImageMetadataMatchingCriteria_WithUploadedAndProcessedImage_ReturnsImageMetadata()
    {
        // Act

        // Upload image to object store
        using var presignedUrlResponse = await _uploadClient.GetAsync("/presigned-url");
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
        await _processingServiceSender.SendMessageAsync(new ServiceBusMessage(messageBody));

        // Assert
        PagedResult<ImageMetadata>? result = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!cts.IsCancellationRequested)
        {
            using var response = await _searchClient.GetAsync("/image-metadata", cts.Token);
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

        var data = result!.Items[0];
        Assert.Equal(blobName, data.BlobName);
        Assert.Equal("Canon", data.ExifData.CameraMake);
    }
}
