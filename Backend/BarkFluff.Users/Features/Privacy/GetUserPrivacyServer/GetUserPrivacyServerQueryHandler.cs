using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Privacy.GetUserPrivacyServer;

public class GetUserPrivacyServerQueryHandler : IRequestHandler<GetUserPrivacyServerQuery, GetUserPrivacyResponse>
{
    private readonly PrivacyStorage _privacyStorage;

    public GetUserPrivacyServerQueryHandler(PrivacyStorage privacyStorage)
    {
        _privacyStorage = privacyStorage;
    }

    public async Task<GetUserPrivacyResponse> Handle(GetUserPrivacyServerQuery request, CancellationToken cancellationToken)
    {
        var privacy = await _privacyStorage.GetOrCreate(request.UserId);

        return new GetUserPrivacyResponse
        {
            Settings = privacy.ToGrpc(),
        };
    }
}
