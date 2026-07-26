using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Infrastructure;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;
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
    private readonly PresenceInterestRegistry _presenceInterest;
    private readonly PresenceOptions _presenceOptions;
    private readonly FederationSwitch _federationSwitch;
    private readonly TypingCoalescer _typingCoalescer;
    private readonly PeerCapabilityCache _peerCapabilities;
    private readonly MetricsCollector _metrics;
    private readonly FederatedFileOptions _fileOptions;
    private readonly ILogger<FederationInternalApiService> _logger;

    public FederationInternalApiService(
        FederationContext context,
        IConfiguration configuration,
        SigningKeyService signingKeyService,
        WellKnownDocumentService wellKnownDocumentService,
        ActiveSigningKeyCache activeSigningKeyCache,
        ServerResolver serverResolver,
        S2SChannelFactory s2sChannelFactory,
        OutboxWriter outboxWriter,
        PresenceInterestRegistry presenceInterest,
        PresenceOptions presenceOptions,
        FederationSwitch federationSwitch,
        TypingCoalescer typingCoalescer,
        PeerCapabilityCache peerCapabilities,
        MetricsCollector metrics,
        FederatedFileOptions fileOptions,
        ILogger<FederationInternalApiService> logger)
    {
        _fileOptions = fileOptions;
        _logger = logger;
        _presenceInterest = presenceInterest;
        _presenceOptions = presenceOptions;
        _federationSwitch = federationSwitch;
        _typingCoalescer = typingCoalescer;
        _peerCapabilities = peerCapabilities;
        _metrics = metrics;
        _context = context;
        _configuration = configuration;
        _signingKeyService = signingKeyService;
        _wellKnownDocumentService = wellKnownDocumentService;
        _activeSigningKeyCache = activeSigningKeyCache;
        _serverResolver = serverResolver;
        _s2sChannelFactory = s2sChannelFactory;
        _outboxWriter = outboxWriter;
    }

    // Интерес инстанса Onliner к remote-presence (этап 4.2/4.3). Набор ПОЛНЫЙ, не дельта:
    // инстансы масштабируются горизонтально, и свести дельты без общего состояния нельзя.
    // Пустой набор — валидное состояние, по нему менеджер стримов закроет S2S-подписку.
    public override Task<SetPresenceInterestResponse> SetPresenceInterest(
        SetPresenceInterestRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "instance_id обязателен"));
        }

        var uuids = new List<Guid>();
        foreach (var raw in request.UserUuids)
        {
            if (Guid.TryParse(raw, out var uuid))
            {
                uuids.Add(uuid);
            }
        }

        // Лимит — защита от разрастания, а не отказ: лишние uuid просто не попадут в подписку.
        if (uuids.Count > _presenceOptions.MaxSubscriptionSize)
        {
            uuids = uuids.Take(_presenceOptions.MaxSubscriptionSize).ToList();
        }

        _presenceInterest.Set(request.InstanceId, uuids);

        return Task.FromResult(new SetPresenceInterestResponse { AcceptedCount = uuids.Count });
    }

    // Принимающая сторона скачивания (этап 3.2): Files своей ноды → сюда → S2S на origin.
    // Deadline на весь вызов НЕ ставим — стрим большого файла законно долгий; вместо этого
    // idle-надзор: молчание origin дольше RemoteFileIdleTimeout обрывает стрим.
    public override async Task FetchRemoteFile(
        FetchRemoteFileRequest request,
        IServerStreamWriter<FetchFileChunk> responseStream,
        ServerCallContext context)
    {
        if (!_federationSwitch.IsActive)
        {
            throw new FederationNotConfiguredException();
        }

        var server = await _serverResolver.ResolveAsync(request.ServerName, context.CancellationToken);
        if (server is null)
        {
            _metrics.Increment("remote_file_fetches.not_resolved");
            throw new RpcException(new Status(StatusCode.PermissionDenied, "нода неизвестна или заблокирована"));
        }

        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        idleCts.CancelAfter(_fileOptions.RemoteFileIdleTimeout);

        long totalSize = 0;
        long received = 0;

        try
        {
            var invoker = await _s2sChannelFactory.GetInvokerAsync(request.ServerName, idleCts.Token);
            var client = new FederationS2SApi.FederationS2SApiClient(invoker);

            using var call = client.FetchFile(
                new FetchFileRequest
                {
                    FileId = request.FileId,
                    RangeFrom = request.RangeFrom,
                    RangeTo = request.RangeTo,
                },
                cancellationToken: idleCts.Token);

            await foreach (var chunk in call.ResponseStream.ReadAllAsync(idleCts.Token))
            {
                // Перезаряжаем idle-таймер на каждом чанке: медленный, но живой origin допустим,
                // молчащий — нет.
                idleCts.CancelAfter(_fileOptions.RemoteFileIdleTimeout);

                if (chunk.TotalSize > 0)
                {
                    totalSize = chunk.TotalSize;
                }

                received += chunk.Data.Length;

                // Защита №44 (первый уровень): origin не может прислать больше, чем сам заявил.
                if (totalSize > 0 && received > totalSize)
                {
                    _metrics.Increment("remote_file_size_mismatch");
                    throw new RpcException(new Status(
                        StatusCode.Aborted, "origin прислал больше байт, чем заявил"));
                }

                await responseStream.WriteAsync(chunk, context.CancellationToken);
            }

            _metrics.Increment("remote_file_fetches.ok");
            _metrics.Add("remote_file_bytes_in", received);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.PermissionDenied or StatusCode.NotFound)
        {
            // Решение origin пробрасываем как есть — вызывающий отличает «нельзя» от «нет файла».
            _metrics.Increment("remote_file_fetches.denied");
            throw;
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            // Сработал idle-надзор, а не отмена вызывающим.
            _metrics.Increment("remote_file_fetches.idle_timeout");
            throw new RpcException(new Status(StatusCode.Unavailable, "origin замолчал"));
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            // Сеть/TLS/резолв — временная недоступность; HTTP-код подберёт этап 3.5.
            _metrics.Increment("remote_file_fetches.error");
            _logger.LogWarning(ex, "Не удалось скачать файл {FileId} с ноды {Server}",
                request.FileId, request.ServerName);
            throw new RpcException(new Status(StatusCode.Unavailable, "origin недоступен"));
        }
    }

    // Исходящий typing (этап 4.4). Fire-and-forget: ни ретраев, ни outbox, ни персиста —
    // потеря индикатора набора некритична, а лишний ретрай стоил бы дороже пользы.
    public override async Task<DeliverTypingOutboundResponse> DeliverTypingOutbound(
        DeliverTypingOutboundRequest request,
        ServerCallContext context)
    {
        var response = new DeliverTypingOutboundResponse();

        // Для вызывающего выключенная федерация — не ошибка: он просто печатает в чате.
        if (!_federationSwitch.IsActive)
        {
            _metrics.Increment("typing_out.not_configured");
            return response;
        }

        if (!Guid.TryParse(request.SenderUuid, out var senderUuid) || string.IsNullOrWhiteSpace(request.ChatId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "chat_id и sender_uuid обязательны"));
        }

        var ownServerName = _configuration["Federation:ServerName"] ?? string.Empty;
        var isCancellation = request.Action == (int)BarkFluff.Proto.Onliner.TypingAction.Cancelled;

        foreach (var destination in request.DestinationServers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Свою ноду в списке назначений игнорируем — симметрично OutboxWriter.
            if (string.Equals(destination, ownServerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!_typingCoalescer.ShouldSend(
                    request.ChatId, senderUuid, destination, _presenceOptions.TypingCoalesceWindow, isCancellation))
            {
                _metrics.Increment("typing_out.coalesced");
                continue;
            }

            await SendTypingAsync(destination, request, context.CancellationToken);
        }

        return response;
    }

    private async Task SendTypingAsync(
        string destination,
        DeliverTypingOutboundRequest request,
        CancellationToken ct)
    {
        try
        {
            if (await _serverResolver.ResolveAsync(destination, ct) is null)
            {
                _metrics.Increment("typing_out.not_resolved");
                return;
            }

            if (!await _peerCapabilities.SupportsAsync(destination, "typing", ct))
            {
                // Партнёр всё равно отбросит вызов — не тратим на него round-trip.
                _metrics.Increment("typing_peer_unsupported");
                return;
            }

            var invoker = await _s2sChannelFactory.GetInvokerAsync(destination, ct);
            var client = new FederationS2SApi.FederationS2SApiClient(invoker);

            await client.DeliverTypingAsync(
                new DeliverTypingRequest
                {
                    ChatId = request.ChatId,
                    SenderUuid = request.SenderUuid,
                    Action = request.Action,
                },
                deadline: DateTime.UtcNow.Add(_presenceOptions.TypingDeadline),
                cancellationToken: ct);

            _metrics.Increment("typing_out.ok");
        }
        catch (Exception)
        {
            // Никаких ретраев: ошибка — это метрика и всё.
            _metrics.Increment("typing_out.error");
        }
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
