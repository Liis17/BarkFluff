using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.GetUserContacts;

public class GetUserContactsCommandHandler : IRequestHandler<GetUserContactsCommand, GetUserContactsResponse>
{
    
    private readonly UsersStorage _usersStorage;

    public GetUserContactsCommandHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<GetUserContactsResponse> Handle(GetUserContactsCommand request, CancellationToken cancellationToken)
    {
        var user = await _usersStorage.GetById(request.UserId);

        if (user is null)
        {
            throw new UserNotFoundException();
        }

        return new GetUserContactsResponse()
        {
            User = user.ToGrpc(),
            Contact = new UserContact()
            {
                Email = user.Contact.Email
            }
        };
    }
}