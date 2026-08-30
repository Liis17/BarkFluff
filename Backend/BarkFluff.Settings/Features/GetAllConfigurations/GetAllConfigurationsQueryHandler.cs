using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Persistence.Services;
using MediatR;

namespace BarkFluff.Settings.Features.GetAllConfigurations;

public sealed class GetAllConfigurationsQueryHandler(SettingsStorage storage)
    : IRequestHandler<GetAllConfigurationsQuery, GetAllConfigurationsResponse>
{
    public async Task<GetAllConfigurationsResponse> Handle(GetAllConfigurationsQuery request, CancellationToken cancellationToken)
    {
        var response = new GetAllConfigurationsResponse();
        response.Configurations.AddRange((await storage.GetAllAsync(cancellationToken)).Select(ConfigurationProtoMapping.ToProto));
        return response;
    }
}
