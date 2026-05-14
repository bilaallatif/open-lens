using MediatR;
using ProcessingService.Application.Interfaces;
using Shared.ObjectStorage.Application;

namespace ProcessingService.Application.ImageMetadata.Commands.CreateImageMetadata;

public record CreateImageMetadataCommand(string BlobName) : IRequest;

public class CreateImageMetadataCommandHandler(
    IObjectStorageClient objectStorageClient,
    IMetadataService metadataService,
    IMetadataRepository metadataRepository
) : IRequestHandler<CreateImageMetadataCommand>
{
    public async Task Handle(
        CreateImageMetadataCommand request,
        CancellationToken cancellationToken
    )
    {
        await using var imageStream = await objectStorageClient.GetObjectStreamAsync(
            request.BlobName
        );
        var metadata = metadataService.GetImageMetadata(imageStream);
        var imageMetadata = new Domain.Entities.ImageMetadata
        {
            BlobName = request.BlobName,
            ExifData = metadata,
        };

        await metadataRepository.CreateAsync(imageMetadata);
    }
}
