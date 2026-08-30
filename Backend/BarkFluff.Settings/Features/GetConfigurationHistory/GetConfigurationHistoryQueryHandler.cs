using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Persistence.Services;
using BarkFluff.Shared.Identity;
using MediatR;

namespace BarkFluff.Settings.Features.GetConfigurationHistory;

public sealed class GetConfigurationHistoryQueryHandler(SettingsStorage storage)
    : IRequestHandler<GetConfigurationHistoryQuery, GetConfigurationHistoryResponse>
{
    public async Task<GetConfigurationHistoryResponse> Handle(GetConfigurationHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!System.Enum.IsDefined(typeof(ServiceId), request.ServiceId))
            throw new ArgumentException($"Неизвестный ServiceId: {request.ServiceId}");
        var serviceId = (ServiceId)request.ServiceId;
        var response = new GetConfigurationHistoryResponse();
        response.Revisions.AddRange((await storage.GetHistoryAsync(request.Section, request.Key, serviceId, request.Count, cancellationToken))
            .Select(revision => ConfigurationProtoMapping.ToProto(revision, serviceId)));
        return response;
    }
}
