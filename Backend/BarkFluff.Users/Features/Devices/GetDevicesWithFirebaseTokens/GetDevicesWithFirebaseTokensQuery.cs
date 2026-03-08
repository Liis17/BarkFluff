using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Devices.GetDevicesWithFirebaseTokens;

public class GetDevicesWithFirebaseTokensQuery : IRequest<GetDevicesWithFirebaseTokensResponse>
{
    public List<long> UserIds { get; set; } = [];
}
