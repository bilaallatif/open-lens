using Azure.Storage.Blobs;
using Shared.ObjectStorage.Infrastructure;
using UploadService.Api.Endpoints;
using UploadService.Application;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddObjectStorageServices();
builder.Services.AddApplicationServices();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "v1"));

    await app.Services.GetRequiredService<BlobContainerClient>().CreateIfNotExistsAsync();
}

app.UseHttpsRedirection();
app.MapHealthChecks("/health");
app.MapUploadEndpoints();

app.Run();
