using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.UpdateStorageLimit;

public class UpdateStorageLimitCommand : IRequest<UpdateStorageLimitResponse>
{
    public long UserId { get; set; }

    public int StorageLimitGb { get; set; }
}
