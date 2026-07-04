using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Users.Features.ChatMutes.GetMutedChats;

public class GetMutedChatsQueryHandler(
    ChatMuteStorage chatMuteStorage,
    UserContext userContext)
    : IRequestHandler<GetMutedChatsQuery, GetMutedChatsResponse>
{
    public async Task<GetMutedChatsResponse> Handle(GetMutedChatsQuery request, CancellationToken cancellationToken)
    {
        var mutes = await chatMuteStorage.GetActiveMutes(userContext.UserId);

        var response = new GetMutedChatsResponse();
        foreach (var mute in mutes)
        {
            var muted = new MutedChat { ChatId = mute.ChatId.ToString() };
            if (mute.MutedUntil is DateTime until)
            {
                muted.MutedUntil = Timestamp.FromDateTime(DateTime.SpecifyKind(until, DateTimeKind.Utc));
            }

            response.Chats.Add(muted);
        }

        return response;
    }
}
