using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace UploadService.IntegrationTests;

public class HealthTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Get_HealthEndpoint_ReturnsOk()
    {
        // Arrange
        var client = factory
            .WithWebHostBuilder(host =>
            {
                host.ConfigureTestServices(services =>
                {
                    services.RemoveAll<BlobContainerClient>();
                    services.AddSingleton(Mock.Of<BlobContainerClient>());
                });
            })
            .CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", responseContent);
    }
}
