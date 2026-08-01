using BarkFluff.Calls.Domain;
using BarkFluff.Calls.Persistence;
using BarkFluff.Calls.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Calls;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Queue.Messages;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Calls.Features.CallLifecycle;

/// <summary>
/// Доменная логика жизненного цикла звонка: ringing → active → ended.
/// Backend делает call-control + выдачу LiveKit-токенов; медиа идёт мимо backend.
/// Доставка ринга — через <see cref="ICallEventDispatcher"/> (fan-out по RabbitMQ на все
/// инстансы; каждый доставляет своим локальным device-scope стримам).
/// </summary>
public class CallLifecycleHandler
{
    private static readonly TimeSpan GroupCallRestartDelay = TimeSpan.FromSeconds(10);

    private readonly CallsContext _db;
    private readonly LiveKitTokenService _tokens;
    private readonly ICallEventDispatcher _dispatcher;
    private readonly CallQualityStore _quality;
    private readonly MessagesServerApi.MessagesServerApiClient _messagesClient;
    private readonly IPublishEndpoint _publish;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<CallLifecycleHandler> _logger;

    public CallLifecycleHandler(
        CallsContext db,
        LiveKitTokenService tokens,
        ICallEventDispatcher dispatcher,
        CallQualityStore quality,
        MessagesServerApi.MessagesServerApiClient messagesClient,
        IPublishEndpoint publish,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<CallLifecycleHandler> logger)
    {
        _db = db;
        _tokens = tokens;
        _dispatcher = dispatcher;
        _quality = quality;
        _messagesClient = messagesClient;
        _publish = publish;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    // ── Исходящий звонок ───────────────────────────────────────────────────

    public async Task<InitiateCallResponse> InitiateAsync(InitiateCallRequest request, CancellationToken ct)
    {
        var callerId = _userContext.UserId;
        var media = request.MediaType.ToDomain();

        var session = new CallSession
        {
            Id = Guid.NewGuid(),
            CallerUserId = callerId,
            Media = media,
            Status = CallStatus.Ringing,
            EndReason = CallEndReasonKind.None,
            StartedAt = DateTime.UtcNow,
        };

        switch (request.TargetCase)
        {
            case InitiateCallRequest.TargetOneofCase.CalleeUserId:
                if (request.CalleeUserId == callerId)
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Нельзя позвонить самому себе"));
                }

                session.CalleeUserId = request.CalleeUserId;
                break;

            case InitiateCallRequest.TargetOneofCase.ChatId:
                var chatId = ParseGuid(request.ChatId, "chat_id");
                await EnsureChatMemberAsync(callerId, chatId, ct);
                await EnsureGroupCallCanBeInitiatedAsync(chatId, ct);
                session.ChatId = chatId;
                break;

            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Не указан получатель звонка"));
        }

        session.RoomName = $"call:{session.Id}";

        _db.CallSessions.Add(session);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (session.IsGroup && IsActiveGroupCallConstraintViolation(ex))
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "В этом чате уже идёт звонок"));
        }

        // Таймаут ринга обрабатывает durable-sweeper (CallRingTimeoutSweeper) — переживает перезапуск инстанса.

        var incoming = new CallEvent
        {
            Incoming = new IncomingCallEvent
            {
                CallId = session.Id.ToString(),
                CallerUserId = callerId,
                ChatId = session.ChatId?.ToString() ?? string.Empty,
                MediaType = media.ToProto(),
                StartedAt = Timestamp.FromDateTime(session.StartedAt),
            }
        };

        var recipients = await GetRingRecipientsAsync(session, ct);
        await RingAsync(recipients, incoming);

        // Push для background/killed app: ринг по in-process стриму доходит только до foreground-устройств.
        if (recipients.Count > 0)
        {
            await _publish.Publish(new IncomingCallPushEvent
            {
                CallId = session.Id,
                CallerUserId = callerId,
                RecipientUserIds = recipients.ToList(),
                ChatId = session.ChatId,
                MediaType = (int)media.ToProto(),
                StartedAt = session.StartedAt,
            }, ct);
        }

        _metrics.Increment("calls_initiated");
        _metrics.Increment(session.IsGroup ? "calls_initiated_group" : "calls_initiated_direct");
        _logger.LogInformation("Звонок {CallId} инициирован пользователем {UserId} ({Kind})",
            session.Id, callerId, session.IsGroup ? "group" : "direct");

        return new InitiateCallResponse
        {
            CallId = session.Id.ToString(),
            LivekitUrl = _tokens.Url,
            AccessToken = CreateToken(session, callerId),
            AudioQuality = _quality.GetAudio(session.Id).ToProto(),
        };
    }

    // ── Приём звонка ───────────────────────────────────────────────────────

    public async Task<AcceptCallResponse> AcceptAsync(string callId, CancellationToken ct)
    {
        var userId = _userContext.UserId;
        var deviceId = RequireDeviceId();
        var session = await LoadAsync(callId, ct);

        if (session.Status == CallStatus.Ended)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Звонок уже завершён"));
        }

        await EnsureCanAnswerAsync(session, userId, ct);

        if (session.Status == CallStatus.Ringing)
        {
            session.Status = CallStatus.Active;
            session.AnsweredAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _metrics.Increment("calls_answered");
        }

        var accepted = new CallEvent { Accepted = new CallAcceptedEvent { CallId = session.Id.ToString(), AcceptedByUserId = userId } };

        // Уведомляем инициатора и гасим ринг на остальных устройствах ответившего.
        await _dispatcher.SendToUserAsync(session.CallerUserId, accepted);
        await _dispatcher.SendToUserExceptDeviceAsync(userId, deviceId, accepted);

        // Гасим push-нотификацию входящего звонка на всех устройствах получателей.
        await PublishDismissAsync(session, await GetRingRecipientsAsync(session, ct), "accepted", ct);

        _logger.LogInformation("Звонок {CallId} принят пользователем {UserId}", session.Id, userId);

        return new AcceptCallResponse
        {
            LivekitUrl = _tokens.Url,
            AccessToken = CreateToken(session, userId),
            AudioQuality = _quality.GetAudio(session.Id).ToProto(),
        };
    }

    // ── Присоединение к идущему звонку (group late-join / второй девайс) ────

    public async Task<JoinCallResponse> JoinAsync(string callId, CancellationToken ct)
    {
        var userId = _userContext.UserId;
        var deviceId = RequireDeviceId();
        var session = await LoadAsync(callId, ct);

        if (session.Status != CallStatus.Active)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Звонок не идёт"));
        }

        await EnsureParticipantAsync(session, userId, ct);

        // Гасим ринг на остальных устройствах присоединившегося.
        var accepted = new CallEvent { Accepted = new CallAcceptedEvent { CallId = session.Id.ToString(), AcceptedByUserId = userId } };
        await _dispatcher.SendToUserExceptDeviceAsync(userId, deviceId, accepted);

        _metrics.Increment("calls_joined");
        _logger.LogInformation("Пользователь {UserId} присоединился к звонку {CallId}", userId, session.Id);

        return new JoinCallResponse
        {
            LivekitUrl = _tokens.Url,
            AccessToken = CreateToken(session, userId),
            AudioQuality = _quality.GetAudio(session.Id).ToProto(),
        };
    }

    // ── Отклонение ─────────────────────────────────────────────────────────

    public async Task<RejectCallResponse> RejectAsync(string callId, CancellationToken ct)
    {
        var userId = _userContext.UserId;
        var session = await LoadAsync(callId, ct);

        var rejected = new CallEvent { Rejected = new CallRejectedEvent { CallId = session.Id.ToString(), RejectedByUserId = userId } };

        if (session.IsGroup)
        {
            await EnsureParticipantAsync(session, userId, ct);
            // В группе один отказ не завершает звонок — лишь гасим ринг на устройствах отказавшегося.
            await _dispatcher.SendToUserAsync(userId, rejected);
        }
        else
        {
            if (userId != session.CalleeUserId)
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Нет доступа к звонку"));
            }

            if (session.Status != CallStatus.Ended)
            {
                Finalize(session, CallEndReasonKind.Rejected, userId);
                await _db.SaveChangesAsync(ct);
                await PostCallSystemMessageAsync(session, ct);
            }

            // Инициатору — «отклонён»; всем устройствам получателя — гасим ринг.
            await _dispatcher.SendToUserAsync(session.CallerUserId, rejected);
            await _dispatcher.SendToUserAsync(userId, rejected);
            _metrics.Increment("calls_rejected");
        }

        // Гасим push-нотификацию на устройствах отклонившего получателя.
        await PublishDismissAsync(session, new[] { userId }, "rejected", ct);

        _logger.LogInformation("Звонок {CallId} отклонён пользователем {UserId}", session.Id, userId);
        return new RejectCallResponse();
    }

    // ── Завершение ─────────────────────────────────────────────────────────

    public async Task<EndCallResponse> EndAsync(string callId, CancellationToken ct)
    {
        var userId = _userContext.UserId;
        var session = await LoadAsync(callId, ct);

        await EnsureParticipantAsync(session, userId, ct);

        if (session.Status != CallStatus.Ended)
        {
            var reason = session.AnsweredAt.HasValue ? CallEndReasonKind.Hangup : CallEndReasonKind.Missed;
            Finalize(session, reason, userId);
            await _db.SaveChangesAsync(ct);
            await NotifyEndedAsync(session, ct);
            await PublishDismissAsync(session, await GetRingRecipientsAsync(session, ct), "ended", ct);
            await PostCallSystemMessageAsync(session, ct);
            _metrics.Increment("calls_ended");
        }

        _logger.LogInformation("Звонок {CallId} завершён пользователем {UserId}", session.Id, userId);
        return new EndCallResponse();
    }

    // ── Качество голоса (общее для всех участников) ─────────────────────────

    public async Task<SetCallAudioQualityResponse> SetAudioQualityAsync(SetCallAudioQualityRequest request, CancellationToken ct)
    {
        var userId = _userContext.UserId;
        var session = await LoadAsync(request.CallId, ct);

        if (session.Status != CallStatus.Active)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Звонок не идёт"));
        }

        await EnsureParticipantAsync(session, userId, ct);

        var quality = request.Quality.ToDomain();
        _quality.SetAudio(session.Id, quality);

        var evt = new CallEvent
        {
            AudioQuality = new CallAudioQualityChangedEvent
            {
                CallId = session.Id.ToString(),
                Quality = quality.ToProto(),
                ChangedByUserId = userId,
            }
        };

        // Рассылаем всем участникам, включая инициатора смены — единый источник истины.
        await _dispatcher.SendToUsersAsync(await GetParticipantsAsync(session), evt);

        _logger.LogInformation("Качество голоса звонка {CallId} → {Quality} (сменил {UserId})",
            session.Id, quality, userId);

        return new SetCallAudioQualityResponse();
    }

    // ── История и активные звонки ──────────────────────────────────────────

    public async Task<ListCallHistoryResponse> ListCallHistoryAsync(ListCallHistoryRequest request, CancellationToken ct)
    {
        var me = _userContext.UserId;
        var limit = request.Limit is > 0 and <= 50 ? request.Limit : 50;

        // v1: личные звонки, где я участник, + групповые, инициированные мной.
        // TODO: полная групповая история (звонки чатов, где я состою) — нужен серверный lookup чатов пользователя.
        var query = _db.CallSessions.AsNoTracking()
            .Where(c => c.Status == CallStatus.Ended)
            .Where(c => (c.ChatId == null && (c.CallerUserId == me || c.CalleeUserId == me))
                     || (c.ChatId != null && c.CallerUserId == me));

        if (request.Filter == CallHistoryFilter.CallHistoryMissed)
        {
            query = query.Where(c => c.EndReason == CallEndReasonKind.Missed);
        }

        if (request.BeforeStartedAt is not null)
        {
            var before = request.BeforeStartedAt.ToDateTime();
            query = query.Where(c => c.StartedAt < before);
        }

        var rows = await query
            .OrderByDescending(c => c.StartedAt)
            .Take(limit + 1)
            .ToListAsync(ct);

        var response = new ListCallHistoryResponse { HasMore = rows.Count > limit };
        foreach (var c in rows.Take(limit))
        {
            response.Items.Add(ToHistoryItem(c, me));
        }

        return response;
    }

    private const int MaxActiveCallsChatIds = 100;

    public async Task<GetActiveCallsResponse> GetActiveCallsAsync(GetActiveCallsRequest request, CancellationToken ct)
    {
        var response = new GetActiveCallsResponse();

        var chatIds = request.ChatIds
            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .Distinct()
            .Take(MaxActiveCallsChatIds)
            .ToList();

        if (chatIds.Count == 0)
        {
            return response;
        }

        // Отдаём только чаты, в которых состоит вызывающий — иначе IDOR на активные звонки чужих чатов.
        var membershipRequest = new CheckChatMembershipRequest { UserId = _userContext.UserId };
        membershipRequest.ChatIds.AddRange(chatIds.Select(id => id.ToString()));
        var membership = await _messagesClient.CheckChatMembershipAsync(membershipRequest, cancellationToken: ct);

        var memberChatIds = chatIds.Where(id => membership.MemberChatIds.Contains(id.ToString())).ToList();
        if (memberChatIds.Count == 0)
        {
            return response;
        }

        var rows = await _db.CallSessions.AsNoTracking()
            .Where(c => c.Status == CallStatus.Active && c.ChatId != null && memberChatIds.Contains(c.ChatId.Value))
            .ToListAsync(ct);

        foreach (var c in rows)
        {
            response.Calls.Add(new ActiveCallItem
            {
                CallId = c.Id.ToString(),
                ChatId = c.ChatId!.Value.ToString(),
                MediaType = c.Media.ToProto(),
                StartedAt = Timestamp.FromDateTime(c.StartedAt),
                // participant_user_ids: в v1 не тянем из LiveKit — клиент делает JoinCall.
            });
        }

        return response;
    }

    private static CallHistoryItem ToHistoryItem(CallSession c, long me)
    {
        var item = new CallHistoryItem
        {
            CallId = c.Id.ToString(),
            ChatId = c.ChatId?.ToString() ?? string.Empty,
            IsGroup = c.IsGroup,
            MediaType = c.Media.ToProto(),
            EndReason = c.EndReason.ToProto(),
            Direction = c.CallerUserId == me ? CallDirection.Outgoing : CallDirection.Incoming,
            StartedAt = Timestamp.FromDateTime(c.StartedAt),
            DurationSeconds = c.DurationSeconds,
        };

        if (!c.IsGroup)
        {
            item.PeerUserId = c.CallerUserId == me ? (c.CalleeUserId ?? 0) : c.CallerUserId;
        }

        if (c.AnsweredAt is { } answered)
        {
            item.AnsweredAt = Timestamp.FromDateTime(answered);
        }

        if (c.EndedAt is { } ended)
        {
            item.EndedAt = Timestamp.FromDateTime(ended);
        }

        return item;
    }

    // ── Системные пути (таймаут / webhooks LiveKit) ────────────────────────

    /// <summary>Таймаут ринга — звонок никто не принял. Вызывается durable-sweeper'ом на любом инстансе.</summary>
    public async Task TimeoutAsync(Guid callId, CancellationToken ct = default)
    {
        // Атомарный захват: ровно один инстанс переведёт Ringing→Ended (условие Status=Ringing в UPDATE).
        // Остальные (или повторные проходы) получат 0 строк и выйдут — доставка событий и системного
        // сообщения происходит ровно один раз даже при параллельных sweeper'ах нескольких инстансов.
        var claimed = await _db.CallSessions
            .Where(c => c.Id == callId && c.Status == CallStatus.Ringing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CallStatus.Ended)
                .SetProperty(c => c.EndReason, CallEndReasonKind.Missed)
                .SetProperty(c => c.EndedAt, DateTime.UtcNow), ct);

        if (claimed == 0)
        {
            return; // уже не Ringing (принят/завершён) или обработан другим инстансом
        }

        var session = await _db.CallSessions.AsNoTracking().FirstAsync(c => c.Id == callId, ct);
        await NotifyEndedAsync(session, ct);
        await PublishDismissAsync(session, await GetRingRecipientsAsync(session, ct), "timeout", ct);
        await PostCallSystemMessageAsync(session, ct);
        _metrics.Increment("calls_missed");
        _logger.LogInformation("Звонок {CallId} помечен пропущенным (таймаут)", callId);
    }

    /// <summary>LiveKit webhook room_finished — комната опустела, финализируем CDR.</summary>
    public async Task HandleRoomFinishedAsync(string roomName)
    {
        var session = await _db.CallSessions.FirstOrDefaultAsync(c => c.RoomName == roomName);
        if (session is null || session.Status == CallStatus.Ended)
        {
            return;
        }

        var reason = session.AnsweredAt.HasValue ? CallEndReasonKind.Hangup : CallEndReasonKind.Missed;
        Finalize(session, reason, endedByUserId: null);
        await _db.SaveChangesAsync();
        await NotifyEndedAsync(session, CancellationToken.None);
        await PublishDismissAsync(session, await GetRingRecipientsAsync(session, CancellationToken.None), "ended", CancellationToken.None);
        await PostCallSystemMessageAsync(session, CancellationToken.None);
        _metrics.Increment("calls_room_finished");
        _logger.LogInformation("Звонок {CallId} завершён по room_finished", session.Id);
    }

    /// <summary>LiveKit webhook participant_joined/left — для группового UI.</summary>
    public async Task HandleParticipantAsync(string roomName, string identity, bool joined)
    {
        var session = await _db.CallSessions.FirstOrDefaultAsync(c => c.RoomName == roomName);
        if (session is null || !long.TryParse(identity, out var userId))
        {
            return;
        }

        var evt = new CallEvent
        {
            Member = new ParticipantEvent
            {
                CallId = session.Id.ToString(),
                UserId = userId,
                Action = joined ? ParticipantAction.ParticipantJoined : ParticipantAction.ParticipantLeft,
            }
        };

        var recipients = (await GetParticipantsAsync(session)).Where(id => id != userId);
        await _dispatcher.SendToUsersAsync(recipients, evt);
    }

    // ── Вспомогательное ────────────────────────────────────────────────────

    /// <summary>Получатели ринга: callee для личного, члены чата кроме инициатора для группового.</summary>
    private async Task<IReadOnlyList<long>> GetRingRecipientsAsync(CallSession session, CancellationToken ct)
    {
        if (session.IsGroup)
        {
            var members = await GetGroupMembersAsync(session.ChatId!.Value, ct);
            return members.Where(id => id != session.CallerUserId).ToList();
        }

        return new[] { session.CalleeUserId!.Value };
    }

    private Task RingAsync(IReadOnlyList<long> recipients, CallEvent incoming)
        => _dispatcher.SendToUsersAsync(recipients, incoming);

    /// <summary>Погасить push-нотификацию входящего звонка на устройствах получателей.</summary>
    private async Task PublishDismissAsync(CallSession session, IReadOnlyList<long> recipients, string reason, CancellationToken ct)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        await _publish.Publish(new CallDismissPushEvent
        {
            CallId = session.Id,
            RecipientUserIds = recipients.ToList(),
            Reason = reason,
        }, ct);
    }

    private async Task NotifyEndedAsync(CallSession session, CancellationToken ct)
    {
        _quality.Remove(session.Id);

        var evt = new CallEvent
        {
            Ended = new CallEndedEvent
            {
                CallId = session.Id.ToString(),
                Reason = session.EndReason.ToProto(),
                DurationSeconds = session.DurationSeconds,
            }
        };

        await _dispatcher.SendToUsersAsync(await GetParticipantsAsync(session), evt);
    }

    /// <summary>Системное сообщение об итоге звонка в чат (best-effort, не блокирует call-control).</summary>
    private async Task PostCallSystemMessageAsync(CallSession session, CancellationToken ct)
    {
        var result = session.EndReason switch
        {
            CallEndReasonKind.Rejected => CallSystemResult.Rejected,
            CallEndReasonKind.Missed => CallSystemResult.Missed,
            _ => session.DurationSeconds > 0
                ? CallSystemResult.Ended
                : CallSystemResult.Missed,
        };

        var request = new PostCallSystemMessageRequest
        {
            SenderUserId = session.CallerUserId,
            Result = result,
            DurationSeconds = session.DurationSeconds,
        };

        if (session.IsGroup)
        {
            request.ChatId = session.ChatId!.Value.ToString();
        }
        else
        {
            request.Person = new PersonCallTarget
            {
                CallerUserId = session.CallerUserId,
                CalleeUserId = session.CalleeUserId!.Value,
            };
        }

        try
        {
            await _messagesClient.PostCallSystemMessageAsync(request, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _metrics.Increment("call_system_message_errors");
            _logger.LogWarning(ex, "Не удалось записать системное сообщение о звонке {CallId}", session.Id);
        }
    }

    private static void Finalize(CallSession session, CallEndReasonKind reason, long? endedByUserId)
    {
        session.Status = CallStatus.Ended;
        session.EndReason = reason;
        session.EndedAt = DateTime.UtcNow;
        session.EndedByUserId = endedByUserId;
    }

    private string CreateToken(CallSession session, long userId)
        => _tokens.CreateRoomToken(session.RoomName, userId.ToString(), displayName: null);

    private async Task<IReadOnlyList<long>> GetParticipantsAsync(CallSession session)
    {
        if (session.IsGroup)
        {
            return await GetGroupMembersAsync(session.ChatId!.Value, CancellationToken.None);
        }

        return new[] { session.CallerUserId, session.CalleeUserId ?? 0 };
    }

    private async Task<IReadOnlyList<long>> GetGroupMembersAsync(Guid chatId, CancellationToken ct)
    {
        var response = await _messagesClient.GetChatMemberIdsAsync(
            new GetChatMemberIdsRequest { ChatId = chatId.ToString() }, cancellationToken: ct);
        return response.UserIds;
    }

    private async Task EnsureCanAnswerAsync(CallSession session, long userId, CancellationToken ct)
    {
        if (session.IsGroup)
        {
            await EnsureChatMemberAsync(userId, session.ChatId!.Value, ct);
        }
        else if (userId != session.CalleeUserId)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Звонок адресован не вам"));
        }
    }

    private async Task EnsureParticipantAsync(CallSession session, long userId, CancellationToken ct)
    {
        if (session.IsGroup)
        {
            await EnsureChatMemberAsync(userId, session.ChatId!.Value, ct);
        }
        else if (userId != session.CallerUserId && userId != session.CalleeUserId)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Нет доступа к звонку"));
        }
    }

    private async Task EnsureChatMemberAsync(long userId, Guid chatId, CancellationToken ct)
    {
        var request = new CheckChatMembershipRequest { UserId = userId };
        request.ChatIds.Add(chatId.ToString());

        var response = await _messagesClient.CheckChatMembershipAsync(request, cancellationToken: ct);
        if (!response.MemberChatIds.Contains(chatId.ToString()))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Вы не состоите в этом чате"));
        }
    }

    private async Task EnsureGroupCallCanBeInitiatedAsync(Guid chatId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var calls = _db.CallSessions.AsNoTracking().Where(c => c.ChatId == chatId);

        if (await calls.AnyAsync(c => c.Status == CallStatus.Ringing || c.Status == CallStatus.Active, ct))
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "В этом чате уже идёт звонок"));
        }

        if (await calls.AnyAsync(c => c.StartedAt > now - GroupCallRestartDelay, ct))
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "Новый звонок в этом чате можно начать через 10 секунд после предыдущего"));
        }
    }

    private static bool IsActiveGroupCallConstraintViolation(DbUpdateException ex)
        => ex.InnerException is Npgsql.PostgresException
        {
            SqlState: "23505",
            ConstraintName: "IX_CallSessions_OneActiveGroupCall",
        };

    private async Task<CallSession> LoadAsync(string callId, CancellationToken ct)
    {
        var id = ParseGuid(callId, "call_id");
        var session = await _db.CallSessions.FirstOrDefaultAsync(c => c.Id == id, ct);
        return session ?? throw new RpcException(new Status(StatusCode.NotFound, "Звонок не найден"));
    }

    private Guid RequireDeviceId()
    {
        if (string.IsNullOrEmpty(_userContext.DeviceId) || !Guid.TryParse(_userContext.DeviceId, out var deviceId))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Для звонков требуется device-id в токене"));
        }

        return deviceId;
    }

    private static Guid ParseGuid(string value, string field)
    {
        if (!Guid.TryParse(value, out var guid))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Некорректный {field}"));
        }

        return guid;
    }
}
