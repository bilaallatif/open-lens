using Microsoft.Extensions.Azure;
using ProcessingService.Application;
using ProcessingService.Infrastructure;
using ProcessingService.Worker.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices();

builder.Services.AddAzureClients(azureBuilder =>
{
    azureBuilder.AddServiceBusClient(builder.Configuration["ServiceBus:ConnectionString"]!);
});

builder.Services.AddHostedService<ImageProcessingWorker>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();
