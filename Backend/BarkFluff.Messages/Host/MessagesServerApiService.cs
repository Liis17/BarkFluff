using BarkFluff.Messages.Features.CheckChatMembership;
using BarkFluff.Messages.Features.ExportData;
using BarkFluff.Messages.Features.GetChatMemberIds;
using BarkFluff.Messages.Features.PostCallSystemMessage;
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

    public override Task<PostCallSystemMessageResponse> PostCallSystemMessage(
        PostCallSystemMessageRequest request,
        ServerCallContext context)
    {
        Guid? chatId = null;
        long? caller = null;
        long? callee = null;

        switch (request.TargetCase)
        {
            case PostCallSystemMessageRequest.TargetOneofCase.ChatId when Guid.TryParse(request.ChatId, out var cid):
                chatId = cid;
                break;
            case PostCallSystemMessageRequest.TargetOneofCase.Person:
                caller = request.Person.CallerUserId;
                callee = request.Person.CalleeUserId;
                break;
            default:
                return Task.FromResult(new PostCallSystemMessageResponse { Posted = false });
        }

        var command = new PostCallSystemMessageCommand
        {
            ChatId = chatId,
            CallerUserId = caller,
            CalleeUserId = callee,
            SenderUserId = request.SenderUserId,
            Result = request.Result,
            DurationSeconds = request.DurationSeconds,
        };

        return _mediator.Send(command, context.CancellationToken);
    }
}
