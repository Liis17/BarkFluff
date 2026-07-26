using BarkFluff.Onliner.Features.GetLocalPresence;
using BarkFluff.Onliner.Features.InjectRemoteTyping;
using BarkFluff.Onliner.Features.UpsertRemoteStatus;
using BarkFluff.Proto.Onliner;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

using DomainStatusTypeId = BarkFluff.Onliner.Domain.Enums.StatusTypeId;

namespace BarkFluff.Onliner.Host;

/// <summary>
/// Мост федеративного presence/typing со стороны Onliner (этап 4.2). Зовёт только
/// Federation своей ноды с service-токеном.
/// </summary>
[Authorize(Policy = nameof(TokenType.Service))]
public class OnlinerServerApiService : OnlinerServerApi.OnlinerServerApiBase
{
    private readonly IMediator _mediator;

    public OnlinerServerApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override Task<UpsertRemoteStatusResponse> UpsertRemoteStatus(
        UpsertRemoteStatusRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserUuid, out var userUuid))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_uuid не является UUID"));
        }

        var command = new UpsertRemoteStatusCommand
        {
            UserUuid = userUuid,
            Status = (DomainStatusTypeId)request.Status,
            LastSeen = request.LastSeen?.ToDateTime() ?? DateTime.UtcNow,
        };

        return _mediator.Send(command, context.CancellationToken);
    }

    public override Task<InjectRemoteTypingResponse> InjectRemoteTyping(
        InjectRemoteTypingRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserUuid, out var senderUuid))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_uuid не является UUID"));
        }

        if (string.IsNullOrWhiteSpace(request.ChatId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "chat_id обязателен"));
        }

        var command = new InjectRemoteTypingCommand
        {
            ChatId = request.ChatId,
            SenderUuid = senderUuid,
            Action = request.Action,
        };

        return _mediator.Send(command, context.CancellationToken);
    }

    public override Task<GetLocalPresenceResponse> GetLocalPresence(
        GetLocalPresenceRequest request,
        ServerCallContext context)
    {
        var query = new GetLocalPresenceQuery { UserIds = request.UserIds.ToList() };

        return _mediator.Send(query, context.CancellationToken);
    }
}
