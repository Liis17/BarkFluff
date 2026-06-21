using BarkFluff.Messages.Features.CheckChatMembership;
using BarkFluff.Messages.Features.ExportData;
using BarkFluff.Messages.Features.GetChatMemberIds;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Messages.Host;

[Authorize(Policy = nameof(TokenType.Service))]
public class MessagesServerApiService : MessagesServerApi.MessagesServerApiBase
{
    private readonly IMediator _mediator;

    public MessagesServerApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override Task<GetUserAllMessagesResponse> GetUserAllMessages(
        GetUserAllMessagesRequest request,
        ServerCallContext context)
    {
        var query = new GetUserAllMessagesQuery
        {
            UserId = request.UserId
        };

        return _mediator.Send(query);
    }

    public override Task<CheckChatMembershipResponse> CheckChatMembership(
        CheckChatMembershipRequest request,
        ServerCallContext context)
    {
        var query = new CheckChatMembershipQuery
        {
            UserId = request.UserId,
            ChatIds = request.ChatIds
        };

        return _mediator.Send(query, context.CancellationToken);
    }

    public override Task<GetChatMemberIdsResponse> GetChatMemberIds(
        GetChatMemberIdsRequest request,
        ServerCallContext context)
    {
        var query = new GetChatMemberIdsQuery
        {
            ChatId = request.ChatId
        };

        return _mediator.Send(query, context.CancellationToken);
    }
}
