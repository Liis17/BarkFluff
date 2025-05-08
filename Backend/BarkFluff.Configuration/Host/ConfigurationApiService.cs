using BarkFluff.Configuration.Features.GetConfiguration;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;
using Grpc.Core;
using MediatR;

namespace BarkFluff.Configuration.Host;

public class ConfigurationApiService : BarkFluff.Proto.Configuration.ConfigurationApi.ConfigurationApiBase
{
    private readonly IMediator _mediator;

    public ConfigurationApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override Task<GetConfigurationResponse> GetConfiguration(GetConfigurationRequest request, ServerCallContext context)
    {
        var command = new GetConfigurationCommand()
        {
            ServiceId = (ServiceId)request.ServiceId
        };
        
        return _mediator.Send(command);
    }
}