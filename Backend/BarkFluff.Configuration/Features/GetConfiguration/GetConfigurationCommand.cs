using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

using MediatR;

namespace BarkFluff.Configuration.Features.GetConfiguration;

public class GetConfigurationCommand : IRequest<GetConfigurationResponse>
{
    public ServiceId ServiceId { get; set; }
}