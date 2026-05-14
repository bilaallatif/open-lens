using MediatR;
using Shared.ObjectStorage.Application;

namespace UploadService.Application.PresignedUrl.Commands.CreatePresignedUrl;

public record CreatePresignedUrlResponse(Uri UploadUrl, string BlobName);

public record CreatePresignedUrlCommand : IRequest<CreatePresignedUrlResponse>;

public class CreatePresignedUrlCommandHandler(IObjectStorageClient objectStorageClient)
    : IRequestHandler<CreatePresignedUrlCommand, CreatePresignedUrlResponse>
{
    public Task<CreatePresignedUrlResponse> Handle(
        CreatePresignedUrlCommand request,
        CancellationToken cancellationToken
    )
    {
        var blobName = $"{Guid.NewGuid()}.jpg";
        return Task.FromResult(
            new CreatePresignedUrlResponse(
                objectStorageClient.GetObjectUploadUri(blobName),
                blobName
            )
        );
    }
}
