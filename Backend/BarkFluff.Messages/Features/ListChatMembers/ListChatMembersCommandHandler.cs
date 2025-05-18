using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;
using MediatR;

namespace BarkFluff.Messages.Features.ListChatMembers;

public class ListChatMembersCommandHandler : IRequestHandler<ListChatMembersCommand, ListChatMembersResponse>
{
    
    private readonly ChatsStorage _chatsStorage;
    private readonly UserContext _userContext;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;

    public ListChatMembersCommandHandler(ChatsStorage chatsStorage, UserContext userContext,
        UsersServerApi.UsersServerApiClient usersServerApiClient)
    {
        _chatsStorage = chatsStorage;
        _userContext = userContext;
        _usersServerApiClient = usersServerApiClient;
    }

    public async Task<ListChatMembersResponse> Handle(ListChatMembersCommand request, CancellationToken cancellationToken)
    {
        var haveAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

        if (!haveAccess)
        {
            throw new NoAccessToChatException();
        }
        
        var members = await _chatsStorage.GetChatMembers(request.ChatId, request.Skip, request.Count);
        var usersResponse = await _usersServerApiClient.ListByIdsAsync(new ListByIdsRequest { Ids = { members.Select(x => x.UserId).ToList() } });


        return new ListChatMembersResponse()
        {
            ChatMembers =
            {
                usersResponse.Users.Select(x => new ListChatMembersResponse.Types.DetailedChatMemberInfo()
                {
                    FirstName = x.FirstName,
                    LastName = x.LastName,
                    GeneralInfo = members.FirstOrDefault(cm => cm.UserId == x.Id)?.ToGrpc()
                })
            }
        };

    }
}