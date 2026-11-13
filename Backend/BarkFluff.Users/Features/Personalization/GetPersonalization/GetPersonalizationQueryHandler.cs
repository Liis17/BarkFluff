using BarkFluff.Proto.Users;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.GetPersonalization;

public class GetPersonalizationQueryHandler : IRequestHandler<GetPersonalizationQuery, GetPersonalizationResponse>
{
    private readonly UserContext _userContext;
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<GetPersonalizationQueryHandler> _logger;

    public GetPersonalizationQueryHandler(
        UserContext userContext,
        PersonalizationStorage personalizationStorage,
        UsersStorage usersStorage,
        ILogger<GetPersonalizationQueryHandler> logger)
    {
        _userContext = userContext;
        _personalizationStorage = personalizationStorage;
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<GetPersonalizationResponse> Handle(GetPersonalizationQuery request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Запрос персонализации для пользователя {UserId}", _userContext.UserId);

        var user = await _usersStorage.GetById(_userContext.UserId);
        if (user is null)
            throw new UserNotFoundException();
        if (user.AccountStatus != Domain.AccountStatus.Active)
            return new GetPersonalizationResponse { Personalization = new UserPersonalizationData() };

        var personalization = await _personalizationStorage.GetOrCreate(_userContext.UserId);

        return new GetPersonalizationResponse
        {
            Personalization = personalization.ToGrpc(),
        };
    }
}
