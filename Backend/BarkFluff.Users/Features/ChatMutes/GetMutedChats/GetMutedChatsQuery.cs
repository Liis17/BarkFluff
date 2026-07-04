using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.ChatMutes.GetMutedChats;

public class GetMutedChatsQuery : IRequest<GetMutedChatsResponse>
{
}
