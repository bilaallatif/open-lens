using MediatR;
using UploadService.Application.PresignedUrl.Commands.CreatePresignedUrl;

namespace UploadService.Api.Endpoints;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/presigned-url",
            async (ISender sender) =>
            {
                var response = await sender.Send(new CreatePresignedUrlCommand());
                return TypedResults.Ok(response);
            }
        );

        // TODO: POST /confirm (local only, ENVIRONMENT=local)
        // After the client uploads directly to Azurite, Angular calls this endpoint to trigger processing.
        // It should publish a BlobCreatedEvent to the Service Bus queue, mirroring the Event Grid event
        // shape used in production, so the processing-service message handler is identical in both environments.
        // Shape: { "subject": "/blobServices/default/containers/{container}/blobs/{blobName}" }

        return app;
    }
}
