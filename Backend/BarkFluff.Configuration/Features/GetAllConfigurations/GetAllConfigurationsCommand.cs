using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.GetAllConfigurations;

public class GetAllConfigurationsCommand : IRequest<GetAllConfigurationsResponse>
{
}
