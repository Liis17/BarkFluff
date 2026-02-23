using BarkFluff.Proto.Files;
using MediatR;

namespace BarkFluff.Files.Features.GetUserStorageInfoServer;

public class GetUserStorageInfoServerCommand : IRequest<GetUserStorageInfoResponse>
{
    public long UserId { get; set; }
}
