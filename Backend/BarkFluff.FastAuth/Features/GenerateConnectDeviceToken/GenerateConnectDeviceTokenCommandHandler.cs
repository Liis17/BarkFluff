using BarkFluff.Proto.FastAuth;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.FastAuth.Features.GenerateConnectDeviceToken;

public class GenerateConnectDeviceTokenCommandHandler : IRequestHandler<GenerateConnectDeviceTokenCommand, GenerateConnectDeviceTokenResponse>
{
    private readonly ILogger<GenerateConnectDeviceTokenCommandHandler> _logger;

    public GenerateConnectDeviceTokenCommandHandler(ILogger<GenerateConnectDeviceTokenCommandHandler> logger)
    {
        _logger = logger;
    }

    public Task<GenerateConnectDeviceTokenResponse> Handle(GenerateConnectDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Попытка вызова неимплементированного метода GenerateConnectDeviceToken");
        throw new NotImplementedException();
    }
}