using BarkFluff.Proto.Users;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.GetPersonalization;

public class GetPersonalizationQueryHandler : IRequestHandler<GetPersonalizationQuery, GetPersonalizationResponse>
{
    private readonly UserContext _userContext;
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly ILogger<GetPersonalizationQueryHandler> _logger;

    public GetPersonalizationQueryHandler(
        UserContext userContext,
        PersonalizationStorage personalizationStorage,
        ILogger<GetPersonalizationQueryHandler> logger)
    {
        _userContext = userContext;
        _personalizationStorage = personalizationStorage;
        _logger = logger;
    }

    public async Task<GetPersonalizationResponse> Handle(GetPersonalizationQuery request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Запрос персонализации для пользователя {UserId}", _userContext.UserId);

        var personalization = await _personalizationStorage.GetOrCreate(_userContext.UserId);

        return new GetPersonalizationResponse
        {
            Personalization = personalization.ToGrpc(),
        };
    }
}
