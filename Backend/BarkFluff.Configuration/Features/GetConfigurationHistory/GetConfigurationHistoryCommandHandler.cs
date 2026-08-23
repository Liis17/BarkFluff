using BarkFluff.Configuration.Persistence;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Configuration.Features.GetConfigurationHistory;

public class GetConfigurationHistoryCommandHandler : IRequestHandler<GetConfigurationHistoryCommand, GetConfigurationHistoryResponse>
{
    private readonly ConfigurationStorage _configurationStorage;

    public GetConfigurationHistoryCommandHandler(ConfigurationStorage configurationStorage)
    {
        _configurationStorage = configurationStorage;
    }

    public async Task<GetConfigurationHistoryResponse> Handle(
        GetConfigurationHistoryCommand request,
        CancellationToken cancellationToken)
    {
        if (!System.Enum.IsDefined(typeof(ServiceId), request.ServiceId))
            throw new ArgumentException($"Неизвестный ServiceId: {request.ServiceId}");

        var revisions = await _configurationStorage.GetConfigurationHistoryAsync(
            request.Section,
            request.Key,
            (ServiceId)request.ServiceId,
            request.Count);

        return new GetConfigurationHistoryResponse
        {
            Revisions =
            {
                revisions.Select(x => new BarkFluff.Proto.Configuration.ConfigurationRevision
                {
                    Id = x.Id,
                    Section = x.Section,
                    Key = x.Key,
                    ServiceId = (int)x.ServiceId,
                    PreviousValue = x.PreviousValue,
                    NewValue = x.NewValue,
                    ChangedAt = Timestamp.FromDateTime(x.ChangedAt),
                    ChangedBy = x.ChangedBy,
                    ChangedFrom = x.ChangedFrom,
                    ChangeKind = x.ChangeKind,
                    SourceRevisionId = x.SourceRevisionId ?? 0
                })
            }
        };
    }
}
