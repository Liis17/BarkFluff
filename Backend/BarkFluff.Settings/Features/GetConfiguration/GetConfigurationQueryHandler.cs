using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Persistence.Services;
using MediatR;

namespace BarkFluff.Settings.Features.GetConfiguration;

public sealed class GetConfigurationQueryHandler(SettingsStorage storage)
    : IRequestHandler<GetConfigurationQuery, GetConfigurationResponse>
{
    public async Task<GetConfigurationResponse> Handle(GetConfigurationQuery request, CancellationToken cancellationToken)
    {
        var response = new GetConfigurationResponse();
        response.Configurations.AddRange((await storage.GetConfigurationAsync(request.ServiceId, cancellationToken))
            .Select(ConfigurationProtoMapping.ToProto));
        return response;
    }
}
