using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.AccountRestrictions;

public sealed class GetAccountRestrictionQueryHandler : IRequestHandler<GetAccountRestrictionQuery, GetAccountRestrictionResponse>
{
    private readonly UsersStorage _usersStorage;

    public GetAccountRestrictionQueryHandler(UsersStorage usersStorage) => _usersStorage = usersStorage;

    public async Task<GetAccountRestrictionResponse> Handle(GetAccountRestrictionQuery request, CancellationToken cancellationToken)
    {
        var user = await _usersStorage.GetById(request.UserId);
        if (user is null)
            throw new UserNotFoundException();

        return new GetAccountRestrictionResponse
        {
            Restriction = user.ToRestrictionInfo(),
            Username = user.Username,
            FirstName = user.FirstName,
            Email = user.Contact?.Email ?? string.Empty,
        };
    }
}
