using BarkFluff.Configuration.Infrastructure;
using BarkFluff.Proto.Configuration;
using Google.Protobuf.WellKnownTypes;
using MediatR;

namespace BarkFluff.Configuration.Features.GetConfiguration;

public class GetConfigurationCommandHandler : IRequestHandler<GetConfigurationCommand, GetConfigurationResponse>
{

    private readonly ConfigurationStorage _configurationStorage;

    public GetConfigurationCommandHandler(ConfigurationStorage configurationStorage)
    {
        _configurationStorage = configurationStorage;
    }

    public async Task<GetConfigurationResponse> Handle(GetConfigurationCommand request, CancellationToken cancellationToken)
    {
        var configurations = await _configurationStorage.GetConfiguration(request.ServiceId);

        return new GetConfigurationResponse()
        {
            Configurations = { configurations.Select(c => new ConfigurationItem()
            {
                Key = c.Key,
                Value = c.Value,
                ServiceId = (int) c.ServiceId,
                EditedAt = Timestamp.FromDateTime(c.EditedAt),
                EditedBy = c.EditedBy,
                EditedFrom = c.EditedFrom,
                Section = c.Section
            }) }
        };
    }
}