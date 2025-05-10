using BarkFluff.Beacon.Configurations;
using BarkFluff.Proto.Beacon;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;
using MediatR;

namespace BarkFluff.Beacon.Features.GetServerInfo;

public class GetServerInfoCommandHandler : IRequestHandler<GetServerInfoCommand, GetServerInfoResponse>
{
    private readonly ServerColorSettings _serverColorSettings;
    private readonly ServerPropsSettings _serverPropsSettings;

    private readonly ConfigurationApi.ConfigurationApiClient _configurationApiClient;

    public GetServerInfoCommandHandler(ServerColorSettings serverColorSettings, ServerPropsSettings serverPropsSettings, 
        ConfigurationApi.ConfigurationApiClient configurationApiClient)
    {
        _serverColorSettings = serverColorSettings;
        _serverPropsSettings = serverPropsSettings;
        _configurationApiClient = configurationApiClient;
    }

    public async Task<GetServerInfoResponse> Handle(GetServerInfoCommand request, CancellationToken cancellationToken)
    {
        var identitySettings = await _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest()
        {
            ServiceId = (int)ServiceId.Identity
        });
        
        var usersSettings = await _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest()
        {
            ServiceId = (int)ServiceId.Users
        });
        
        var filesSettings = await _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest()
        {
            ServiceId = (int)ServiceId.Files
        });
        
        return new GetServerInfoResponse
        {
            Name = _serverPropsSettings.Name,
            Description = _serverPropsSettings.Description,
            
            Color = new ServerColor
            {
                HardHex = _serverColorSettings.Hard,
                LiteHex = _serverColorSettings.Lite,
                MainHex = _serverColorSettings.Main
            },
            
            Files = ParseService(ServiceId.Files, filesSettings.Configurations.ToList()),
            Identity = ParseService(ServiceId.Identity, identitySettings.Configurations.ToList()),
            Users = ParseService(ServiceId.Users, usersSettings.Configurations.ToList()),
        };
    }

    private Service ParseService(ServiceId id, List<ConfigurationItem> settings)
    {
        return new Service
        {
            Name = nameof(id),
            Endpoint = new ServiceEndpoint
            {
                Host = settings.First(x => x.Section == "RunSettings"
                                           && x.Key == "Host").Value,
                Port = int.Parse(settings.First(x => x.Section == "RunSettings"
                                                     && x.Key == "Port").Value)
            },
            Status = ServiceStatus.Healthy,
            TlsEnabled = false
        };
    }
}