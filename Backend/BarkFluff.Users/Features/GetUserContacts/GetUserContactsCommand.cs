using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.GetUserContacts;

public class GetUserContactsCommand : IRequest<GetUserContactsResponse>
{
    public long UserId { get; set; }
}