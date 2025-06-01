using BarkFluff.Proto.FastAuth;
using MediatR;

namespace BarkFluff.FastAuth.Features.GenerateConnectDeviceToken;

public class GenerateConnectDeviceTokenCommandHandler : IRequestHandler<GenerateConnectDeviceTokenCommand, GenerateConnectDeviceTokenResponse>
{
    public Task<GenerateConnectDeviceTokenResponse> Handle(GenerateConnectDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}