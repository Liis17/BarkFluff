using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Infrastructure;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.Proto.Federation;
using BarkFluff.Proto.FederationInternal;
using BarkFluff.Shared.Exceptions.Federation;
using BarkFluff.Shared.Identity;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.Host;

// Внутренний API Federation-сервиса. Авторизация — XAuth, TokenType.Service.
[Authorize(Policy = nameof(TokenType.Service))]
public class FederationInternalApiService : FederationInternalApi.FederationInternalApiBase
{
    private readonly FederationContext _context;
    private readonly IConfiguration _configuration;
    private readonly SigningKeyService _signingKeyService;
    private readonly WellKnownDocumentService _wellKnownDocumentService;
    private readonly ActiveSigningKeyCache _activeSigningKeyCache;
    private readonly ServerResolver _serverResolver;
    private readonly S2SChannelFactory _s2sChannelFactory;
    private readonly OutboxWriter _outboxWriter;

    public FederationInternalApiService(
        FederationContext context,
        IConfiguration configuration,
        SigningKeyService signingKeyService,
        WellKnownDocumentService wellKnownDocumentService,
        ActiveSigningKeyCache activeSigningKeyCache,
        ServerResolver serverResolver,
        S2SChannelFactory s2sChannelFactory,
        OutboxWriter outboxWriter)
    {
        _context = context;
        _configuration = configuration;
        _signingKeyService = signingKeyService;
        _wellKnownDocumentService = wellKnownDocumentService;
        _activeSigningKeyCache = activeSigningKeyCache;
        _serverResolver = serverResolver;
        _s2sChannelFactory = s2sChannelFactory;
        _outboxWriter = outboxWriter;
    }

    public override async Task<RotateSigningKeyResponse> RotateSigningKey(RotateSigningKeyRequest request, ServerCallContext context)
    {
        var (newKey, oldKey) = await _signingKeyService.RotateAsync(context.CancellationToken);

        // Well-known после ротации публикует оба ключа и подписан новым; исходящие XFed-подписи —
        // новым активным ключом немедленно.
        await _wellKnownDocumentService.RebuildAsync(context.CancellationToken);
        await _activeSigningKeyCache.RefreshAsync(context.CancellationToken);

        return new RotateSigningKeyResponse
        {
            NewKeyId = newKey.KeyId,
            OldKeyId = oldKey.KeyId,
            OldKeyExpiresAt = Timestamp.FromDateTime(DateTime.SpecifyKind(oldKey.ExpiredAt!.Value, DateTimeKind.Utc)),
        };
    }

    public override async Task<GetKnownServersResponse> GetKnownServers(GetKnownServersRequest request, ServerCallContext context)
    {
        var servers = await _context.KnownServers.Include(s => s.Keys).ToListAsync(context.CancellationToken);

        var response = new GetKnownServersResponse();
        response.Servers.AddRange(servers.Select(ToKnownServerInfo));
        return response;
    }

    public override async Task<UpsertManualPeerResponse> UpsertManualPeer(UpsertManualPeerRequest request, ServerCallContext context)
    {
        if (!ServernameValidator.TryNormalizeSyntax(request.ServerName, out var normalized))
            throw new InvalidServernameException();

        var existing = await _context.KnownServers
            .Include(s => s.Keys)
            .FirstOrDefaultAsync(s => s.ServerName == normalized, context.CancellationToken);

        var now = DateTime.UtcNow;
        var isNew = existing == null;
        var oldEndpoint = existing?.FederationEndpoint;
        var oldSpki = existing?.TlsSpkiSha256;

        if (existing == null)
        {
            existing = new KnownServer
            {
                ServerName = normalized,
                Source = KnownServerSource.Manual,
                Status = KnownServerStatus.Active,
                FirstSeenAt = now,
            };
            _context.KnownServers.Add(existing);
        }

        existing.FederationEndpoint = request.FederationEndpoint;
        existing.TlsSpkiSha256 = request.TlsSpkiSha256.ToArray();
        existing.Source = KnownServerSource.Manual;
        existing.LastSeenAt = now;
        existing.LastKeyRefreshAt = now;

        // P1-09: единая reconciliation (тот же путь, что у discovery). Admin-набор ключей
        // авторитетен: присутствующие синхронизируются, исчезнувшие отзываются, новые добавляются.
        var docKeys = request.Keys
            .Select(k => new RemoteSigningKey(
                k.KeyId,
                k.PublicKey.ToByteArray(),
                k.ExpiredAt != null ? k.ExpiredAt.ToDateTime() : null))
            .ToList();
        KnownServerKeyReconciler.Reconcile(existing, docKeys, now);

        await _context.SaveChangesAsync(context.CancellationToken);

        // P1-08: смена endpoint/SPKI → сбросить кешированный S2S-канал.
        var newSpki = request.TlsSpkiSha256.ToArray();
        if (!isNew && (oldEndpoint != request.FederationEndpoint || !(oldSpki ?? []).SequenceEqual(newSpki)))
            _s2sChannelFactory.Invalidate(normalized);

        return new UpsertManualPeerResponse();
    }

    public override async Task<SetServerBlockedResponse> SetServerBlocked(SetServerBlockedRequest request, ServerCallContext context)
    {
        if (!ServernameValidator.TryNormalizeSyntax(request.ServerName, out var normalized))
            throw new InvalidServernameException();

        var server = await _context.KnownServers.FirstOrDefaultAsync(s => s.ServerName == normalized, context.CancellationToken)
            ?? throw new FederationPeerNotFoundException();

        server.Status = request.Blocked ? KnownServerStatus.Blocked : KnownServerStatus.Active;
        await _context.SaveChangesAsync(context.CancellationToken);

        return new SetServerBlockedResponse();
    }

    public override async Task<GetFederationStatusResponse> GetFederationStatus(GetFederationStatusRequest request, ServerCallContext context)
    {
        var ownKeys = await _signingKeyService.GetNonRevokedKeysAsync(context.CancellationToken);
        var activeCount = await _context.KnownServers.CountAsync(s => s.Status == KnownServerStatus.Active, context.CancellationToken);

        var response = new GetFederationStatusResponse
        {
            ServerName = _configuration["Federation:ServerName"] ?? string.Empty,
            Enabled = string.Equals(_configuration["Federation:Enabled"], "true", StringComparison.OrdinalIgnoreCase),
            OutboxPending = await _context.Outbox.LongCountAsync(o => o.Status == OutboxStatus.Pending, context.CancellationToken),
            OutboxDeadletter = await _context.Outbox.LongCountAsync(o => o.Status == OutboxStatus.DeadLetter, context.CancellationToken),
            KnownServersActive = activeCount,
        };

        response.OwnKeys.AddRange(ownKeys.Select(k => new SigningKey
        {
            KeyId = k.KeyId,
            PublicKey = ByteString.CopyFrom(k.PublicKey),
            ExpiredAt = k.ExpiredAt.HasValue
                ? Timestamp.FromDateTime(DateTime.SpecifyKind(k.ExpiredAt.Value, DateTimeKind.Utc))
                : null,
        }));

        return response;
    }

    // Federation.ResolveRemoteUser (этап 2.1): parse FID/UUID+server → ServerResolver →
    // подписанный S2S GetUserProfile на ноду-владельца → отдача ответа Users.
    // Federation не пишет пользовательское состояние — RemoteUsers кеширует вызывающий (Users).
    public override async Task<ResolveRemoteUserResponse> ResolveRemoteUser(ResolveRemoteUserRequest request, ServerCallContext context)
    {
        // Federation:Enabled — гейтинг: при выключенной федерации честно отдаём found=false,
        // никаких сетевых походок.
        if (!string.Equals(_configuration["Federation:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
            return new ResolveRemoteUserResponse { Found = false };

        string? username = null;
        string? uuid = null;
        string? serverName = null;

        switch (request.UserCase)
        {
            case ResolveRemoteUserRequest.UserOneofCase.Fid:
                if (!TryParseFid(request.Fid, out var parsedUsername, out var parsedServer))
                    return new ResolveRemoteUserResponse { Found = false };
                username = parsedUsername;
                serverName = parsedServer;
                break;
            case ResolveRemoteUserRequest.UserOneofCase.Uuid:
                uuid = request.Uuid;
                serverName = request.ServerName;
                if (string.IsNullOrWhiteSpace(serverName) || !ServernameValidator.TryNormalizeSyntax(serverName, out var normalized))
                    return new ResolveRemoteUserResponse { Found = false };
                serverName = normalized;
                break;
            default:
                return new ResolveRemoteUserResponse { Found = false };
        }

        if (string.IsNullOrWhiteSpace(serverName))
            return new ResolveRemoteUserResponse { Found = false };

        var server = await _serverResolver.ResolveAsync(serverName, context.CancellationToken);
        if (server is null)
            return new ResolveRemoteUserResponse { Found = false };

        var s2sRequest = new GetUserProfileRequest();
        if (username is not null)
            s2sRequest.Username = username;
        else
            s2sRequest.Uuid = uuid!;

        try
        {
            var invoker = await _s2sChannelFactory.GetInvokerAsync(server.ServerName, context.CancellationToken);
            var s2sClient = new FederationS2SApi.FederationS2SApiClient(invoker);
            var profile = await s2sClient.GetUserProfileAsync(s2sRequest, cancellationToken: context.CancellationToken);

            if (!profile.Found)
                return new ResolveRemoteUserResponse { Found = false, ServerName = server.ServerName };

            return new ResolveRemoteUserResponse
            {
                Found = true,
                Profile = profile,
                ServerName = server.ServerName,
            };
        }
        catch (RpcException)
        {
            // Сетевая ошибка / пир недоступен — вызывающий (Users) получит found=false и не закеширует мусор.
            return new ResolveRemoteUserResponse { Found = false, ServerName = server.ServerName };
        }
    }

    // Простой FID-парсер внутри Federation: @username:servername → punycode A-label.
    // Полную валидацию (username regex, anti-SSRF) делает и origin-сервер, и ServerResolver.
    private static bool TryParseFid(string? raw, out string username, out string serverName)
    {
        username = string.Empty;
        serverName = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('@'))
            trimmed = trimmed[1..];

        var colon = trimmed.IndexOf(':');
        if (colon <= 0 || colon == trimmed.Length - 1)
            return false;

        username = trimmed[..colon];
        var rawServer = trimmed[(colon + 1)..];

        return ServernameValidator.TryNormalizeSyntax(rawServer, out serverName);
    }

    // EnqueueOutbound (этап 2.2): прямая постановка в outbox. Federation подписывает событие
    // активным ключом и кладёт по одной строке на каждую ноду из destinations.
    public override async Task<EnqueueOutboundResponse> EnqueueOutbound(EnqueueOutboundRequest request, ServerCallContext context)
    {
        if (request.Event is null)
            throw new ArgumentException("event is required");

        if (request.Destinations.Count == 0)
            return new EnqueueOutboundResponse { EventId = request.Event.EventId, Enqueued = 0 };

        Guid? chatId = request.Event.PayloadCase switch
        {
            FederationEvent.PayloadOneofCase.ChatCreated => Guid.TryParse(request.Event.ChatCreated.ChatId, out var c1) ? c1 : null,
            FederationEvent.PayloadOneofCase.NewMessage => Guid.TryParse(request.Event.NewMessage.ChatId, out var c2) ? c2 : null,
            FederationEvent.PayloadOneofCase.MessageEdited => Guid.TryParse(request.Event.MessageEdited.ChatId, out var c3) ? c3 : null,
            FederationEvent.PayloadOneofCase.MessageDeleted => Guid.TryParse(request.Event.MessageDeleted.ChatId, out var c4) ? c4 : null,
            FederationEvent.PayloadOneofCase.MessagesRead => Guid.TryParse(request.Event.MessagesRead.ChatId, out var c5) ? c5 : null,
            _ => null,
        };

        var beforeCount = await _context.Outbox.CountAsync(context.CancellationToken);
        await _outboxWriter.EnqueueSignedAsync(request.Event, chatId, request.Destinations.ToList(), context.CancellationToken);
        var afterCount = await _context.Outbox.CountAsync(context.CancellationToken);

        return new EnqueueOutboundResponse
        {
            EventId = request.Event.EventId,
            Enqueued = afterCount - beforeCount,
        };
    }

    private static KnownServerInfo ToKnownServerInfo(KnownServer server)
    {
        var info = new KnownServerInfo
        {
            ServerName = server.ServerName,
            FederationEndpoint = server.FederationEndpoint,
            Source = server.Source.ToString(),
            Status = server.Status.ToString(),
            FirstSeenAt = Timestamp.FromDateTime(DateTime.SpecifyKind(server.FirstSeenAt, DateTimeKind.Utc)),
            LastSeenAt = Timestamp.FromDateTime(DateTime.SpecifyKind(server.LastSeenAt, DateTimeKind.Utc)),
        };

        info.Keys.AddRange(server.Keys.Select(k => new SigningKey
        {
            KeyId = k.KeyId,
            PublicKey = ByteString.CopyFrom(k.PublicKey),
            ExpiredAt = k.ExpiredAt.HasValue
                ? Timestamp.FromDateTime(DateTime.SpecifyKind(k.ExpiredAt.Value, DateTimeKind.Utc))
                : null,
        }));

        return info;
    }
}
