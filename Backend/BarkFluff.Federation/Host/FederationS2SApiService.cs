using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Proto.Users;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.Host;

// Авторизация — НЕ XAuth: Ed25519-подпись S2S-запросов (XFed, этап 1.3).
// В 1.1/1.2 Ping и GetServerKeys временно доступны без подписи (GetServerKeys — bootstrap-канал,
// останется неподписанным и после 1.3, см. docs/rearch/phase-1/step-1.2-keys-wellknown.md).
public class FederationS2SApiService : FederationS2SApi.FederationS2SApiBase
{
    private readonly IConfiguration _configuration;
    private readonly SigningKeyService _signingKeyService;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly FederationContext _context;
    private readonly MetricsCollector _metrics;

    public FederationS2SApiService(
        IConfiguration configuration,
        SigningKeyService signingKeyService,
        UsersServerApi.UsersServerApiClient usersClient,
        FederationContext context,
        MetricsCollector metrics)
    {
        _configuration = configuration;
        _signingKeyService = signingKeyService;
        _usersClient = usersClient;
        _context = context;
        _metrics = metrics;
    }

    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
    {
        var response = new PingResponse
        {
            ServerName = _configuration["Federation:ServerName"] ?? string.Empty,
            ServerTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        };
        response.ProtocolVersions.Add(1);

        return Task.FromResult(response);
    }

    public override async Task<GetServerKeysResponse> GetServerKeys(GetServerKeysRequest request, ServerCallContext context)
    {
        var keys = await _signingKeyService.GetNonRevokedKeysAsync(context.CancellationToken);

        var response = new GetServerKeysResponse
        {
            ServerName = _configuration["Federation:ServerName"] ?? string.Empty,
        };

        response.Keys.AddRange(keys.Select(k => new SigningKey
        {
            KeyId = k.KeyId,
            PublicKey = Google.Protobuf.ByteString.CopyFrom(k.PublicKey),
            ExpiredAt = k.ExpiredAt.HasValue
                ? Timestamp.FromDateTime(DateTime.SpecifyKind(k.ExpiredAt.Value, DateTimeKind.Utc))
                : null,
        }));

        return response;
    }

    // S2S профиль пользователя этой ноды (этап 2.1). XFed уже проверил подпись в per-service интерсепторе —
    // здесь только privacy-фильтрованная отдача через Users.GetFederatedProfile.
    public override async Task<GetUserProfileResponse> GetUserProfile(GetUserProfileRequest request, ServerCallContext context)
    {
        var usersRequest = new GetFederatedProfileRequest();
        switch (request.UserCase)
        {
            case GetUserProfileRequest.UserOneofCase.Username:
                usersRequest.Username = request.Username;
                break;
            case GetUserProfileRequest.UserOneofCase.Uuid:
                usersRequest.Uuid = request.Uuid;
                break;
            default:
                return new GetUserProfileResponse { Found = false };
        }

        var profile = await _usersClient.GetFederatedProfileAsync(usersRequest, cancellationToken: context.CancellationToken);

        if (!profile.Found)
            return new GetUserProfileResponse { Found = false };

        var response = new GetUserProfileResponse
        {
            Found = true,
            Uuid = profile.Uuid,
            Username = profile.Username,
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Bio = profile.Bio,
        };

        if (!string.IsNullOrEmpty(profile.AvatarFileId))
        {
            response.Avatar = new FederatedFileRef
            {
                OriginServer = _configuration["Federation:ServerName"] ?? string.Empty,
                FileId = profile.AvatarFileId,
            };
        }

        return response;
    }

    // Основной канал доставки федеративных событий (этап 2.2). Для каждого события:
    // 1. origin_server события == x-bf-origin (XFed проверил подпись запроса).
    // 2. ProcessedEvents содержит event_id → ALREADY_PROCESSED.
    // 3. Проверка origin_signature ключами origin → REJECTED при невалидной.
    // 4. uuid/server_name автора внутри payload принадлежит origin.
    // 5. Маршрутизация → внутренний вызов. В этом этапе обработчики чатовых возвращают
    //    RETRY:NotImplementedYet (импорт-RPC Messages — этап 2.3).
    // 6. Успех → запись ProcessedEvents + OK.
    public override async Task<DeliverEventsResponse> DeliverEvents(DeliverEventsRequest request, ServerCallContext context)
    {
        var origin = context.UserState.TryGetValue("xfed-origin", out var originObj) ? originObj as string : null;
        if (string.IsNullOrEmpty(origin))
        {
            // Защита: интерсептор уже должен был провести — но на всякий случай отвечаем REJECTED для всех.
            return AllRejected(request, "missing_origin");
        }

        var response = new DeliverEventsResponse();
        foreach (var evt in request.Events)
        {
            var result = await ProcessEventAsync(evt, origin, context.CancellationToken);
            response.Results.Add(result);
        }

        return response;
    }

    private async Task<EventResult> ProcessEventAsync(FederationEvent evt, string origin, CancellationToken ct)
    {
        if (!Guid.TryParse(evt.EventId, out var eventId))
            return Rejected(evt, "invalid_event_id");

        if (!string.Equals(evt.OriginServer, origin, StringComparison.OrdinalIgnoreCase))
        {
            _metrics.Increment("events_rejected.origin_mismatch");
            return Rejected(evt, "origin_mismatch");
        }

        // Дедуп.
        var alreadyProcessed = await _context.ProcessedEvents.AnyAsync(e => e.EventId == eventId, ct);
        if (alreadyProcessed)
        {
            _metrics.Increment("events_duplicate");
            return new EventResult
            {
                EventId = evt.EventId,
                Status = EventStatus.AlreadyProcessed,
            };
        }

        // Проверка подписи события.
        var key = await _context.KnownServerKeys.FirstOrDefaultAsync(k => k.ServerName == origin && k.KeyId == evt.OriginKeyId, ct);
        if (key is null || key.RevokedAt is not null
            || (key.ExpiredAt is not null && key.ExpiredAt.Value < DateTime.UtcNow))
        {
            _metrics.Increment("events_rejected.unknown_key");
            return Rejected(evt, "unknown_key");
        }

        if (!EventSigner.Verify(evt, key.PublicKey))
        {
            _metrics.Increment("events_rejected.invalid_signature");
            return Rejected(evt, "invalid_signature");
        }

        // "Нода говорит только за своих" — server_name автора в payload обязан быть origin.
        if (!PayloadAuthorBelongsToOrigin(evt, origin))
        {
            _metrics.Increment("events_rejected.author_not_origin");
            return Rejected(evt, "author_not_origin");
        }

        // Маршрутизация. В этапе 2.2 — все чатовые возвращают NotImplementedYet (импорт в Messages — 2.3).
        var routeResult = RouteToInternal(evt, origin);
        if (routeResult == EventStatus.Ok)
        {
            // Записываем идемпотентность только после успеха (RETRY не должен индексироваться).
            _context.ProcessedEvents.Add(new ProcessedEvent
            {
                EventId = eventId,
                OriginServer = origin,
                ReceivedAt = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync(ct);
            _metrics.Increment("events_received." + evt.PayloadCase.ToString().ToLowerInvariant());
        }

        return new EventResult { EventId = evt.EventId, Status = routeResult };
    }

    private EventStatus RouteToInternal(FederationEvent evt, string origin)
    {
        // Этап 2.2: импорт-RPC в Messages ещё не реализован (2.3). Все чатовые payload'ы возвращают RETRY.
        // Профильные payload'ы (2.9) — здесь тоже RETRY.
        switch (evt.PayloadCase)
        {
            case FederationEvent.PayloadOneofCase.ChatCreated:
            case FederationEvent.PayloadOneofCase.NewMessage:
            case FederationEvent.PayloadOneofCase.MessageEdited:
            case FederationEvent.PayloadOneofCase.MessageDeleted:
            case FederationEvent.PayloadOneofCase.MessagesRead:
            case FederationEvent.PayloadOneofCase.ProfileChanged:
            case FederationEvent.PayloadOneofCase.UserDeactivated:
                return EventStatus.Retry;
            default:
                return EventStatus.Rejected;
        }
    }

    private static bool PayloadAuthorBelongsToOrigin(FederationEvent evt, string origin)
    {
        FederatedUser? author = evt.PayloadCase switch
        {
            FederationEvent.PayloadOneofCase.ChatCreated => evt.ChatCreated.Initiator,
            FederationEvent.PayloadOneofCase.NewMessage => evt.NewMessage.Sender,
            FederationEvent.PayloadOneofCase.MessageEdited => null,
            FederationEvent.PayloadOneofCase.MessageDeleted => null,
            FederationEvent.PayloadOneofCase.MessagesRead => null,
            FederationEvent.PayloadOneofCase.ProfileChanged => evt.ProfileChanged.User,
            FederationEvent.PayloadOneofCase.UserDeactivated => null,
            _ => null,
        };

        // Если payload не содержит автора — проверка не применяется (REJECTED будет по другому коду).
        if (author is null)
            return true;

        return string.Equals(author.ServerName, origin, StringComparison.OrdinalIgnoreCase);
    }

    private static EventResult Rejected(FederationEvent evt, string code)
        => new() { EventId = evt.EventId, Status = EventStatus.Rejected, ErrorCode = code };

    private static DeliverEventsResponse AllRejected(DeliverEventsRequest request, string code)
    {
        var response = new DeliverEventsResponse();
        foreach (var evt in request.Events)
            response.Results.Add(Rejected(evt, code));
        return response;
    }
}
