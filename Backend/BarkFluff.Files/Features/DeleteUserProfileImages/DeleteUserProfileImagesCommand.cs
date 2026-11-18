using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.DeleteUserProfileImages;

public sealed class DeleteUserProfileImagesCommand : IRequest<DeleteUserProfileImagesResponse>
{
    public long UserId { get; init; }
}
