using Azure.Storage.Blobs;
using Shared.ObjectStorage.Infrastructure;
using UploadService.Api.Endpoints;
using UploadService.Application;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddObjectStorageServices();
builder.Services.AddApplicationServices();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "v1"));

    await app.Services.GetRequiredService<BlobContainerClient>().CreateIfNotExistsAsync();
}

app.UseHttpsRedirection();
app.MapDefaultEndpoints();
app.MapUploadEndpoints();

app.Run();
