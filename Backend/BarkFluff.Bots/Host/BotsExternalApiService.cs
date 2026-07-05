using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Bots;
using BarkFluff.Proto.Users;

using Grpc.Core;

using Microsoft.AspNetCore.Authorization;

using MessagesProto = BarkFluff.Proto.Messages;

namespace BarkFluff.Bots.Host;

/// <summary>
/// Внешний Bot API (gRPC). Аутентификация — BotTokenInterceptor по метадате x-bot-token
/// (вешается через AddServiceOptions только на этот сервис), XAuth не используется.
/// </summary>
[AllowAnonymous]
public class BotsExternalApiService : BotsExternalApi.BotsExternalApiBase
{
    private const int UpdatesBatchSize = 100;
    private static readonly TimeSpan LiveWaitInterval = TimeSpan.FromSeconds(25);

    private readonly MessagesProto.MessagesServerApi.MessagesServerApiClient _messagesClient;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly BotUpdateNotifier _notifier;
    private readonly BotPollingGuard _pollingGuard;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<BotsExternalApiService> _logger;

    public BotsExternalApiService(
        MessagesProto.MessagesServerApi.MessagesServerApiClient messagesClient,
        UsersServerApi.UsersServerApiClient usersClient,
        BotUpdateNotifier notifier,
        BotPollingGuard pollingGuard,
        IServiceScopeFactory scopeFactory,
        MetricsCollector metrics,
        ILogger<BotsExternalApiService> logger)
    {
        _messagesClient = messagesClient;
        _usersClient = usersClient;
        _notifier = notifier;
        _pollingGuard = pollingGuard;
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _logger = logger;
    }

    public override Task<GetMeResponse> GetMe(GetMeRequest request, ServerCallContext context)
    {
        var bot = context.GetBot();

        return Task.FromResult(new GetMeResponse
        {
            Id = bot.Id,
            IsBot = true,
            FirstName = bot.Name,
            Username = bot.Username,
        });
    }

    public override async Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context)
    {
        var bot = context.GetBot();
        _metrics.Increment("bot_api_messages_sent");

        // Авторизацию отправки выполняет SendMessageServer: членство бота в чате (chat_id)
        // и запрет инициации личного чата (user_id) — бот отвечает только в существующие чаты.
        var serverRequest = new MessagesProto.SendMessageServerRequest
        {
            SenderUserId = bot.Id,
            Message = new MessagesProto.OutgoingMessage
            {
                Text = request.Text ?? string.Empty,
            },
        };
        serverRequest.Message.FilesIds.AddRange(request.FileIds);

        switch (request.TargetCase)
        {
            case SendMessageRequest.TargetOneofCase.ChatId:
                serverRequest.ChatId = request.ChatId;
                break;
            case SendMessageRequest.TargetOneofCase.UserId:
                serverRequest.UserId = request.UserId;
                break;
            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument, "chat_id или user_id обязателен"));
        }

        var response = await _messagesClient.SendMessageServerAsync(serverRequest, cancellationToken: context.CancellationToken);

        return new SendMessageResponse
        {
            MessageId = response.Message.Id,
            ChatId = request.TargetCase == SendMessageRequest.TargetOneofCase.ChatId ? request.ChatId : string.Empty,
            SentAt = response.Message.SentAt,
        };
    }

    public override async Task<GetUserInfoResponse> GetUserInfo(GetUserInfoRequest request, ServerCallContext context)
    {
        context.GetBot();
        _metrics.Increment("bot_api_user_info_requests");

        switch (request.UserCase)
        {
            case GetUserInfoRequest.UserOneofCase.Username:
            {
                // Privacy применяет Users
                var response = await _usersClient.GetUserByUsernameAsync(
                    new GetUserByUsernameRequest { Username = request.Username },
                    cancellationToken: context.CancellationToken);

                return new GetUserInfoResponse
                {
                    Id = response.Id,
                    Username = request.Username,
                    FirstName = response.FirstName,
                    LastName = response.LastName,
                    Bio = response.Bio,
                    AvatarUrl = response.ProfilePicture,
                    IsBot = response.IsBot,
                };
            }

            case GetUserInfoRequest.UserOneofCase.UserId:
            {
                var response = await _usersClient.GetByIdAsync(
                    new GetByIdRequest { UserId = request.UserId },
                    cancellationToken: context.CancellationToken);

                // Только публичные поля
                return new GetUserInfoResponse
                {
                    Id = response.User.Id,
                    Username = response.User.Username,
                    FirstName = response.User.FirstName,
                    LastName = response.User.LastName,
                    Bio = response.User.Bio,
                    AvatarUrl = response.User.ProfilePicture,
                    IsBot = response.User.IsBot,
                };
            }

            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id или username обязателен"));
        }
    }

    public override async Task SubscribeUpdates(
        SubscribeUpdatesRequest request,
        IServerStreamWriter<BotUpdate> responseStream,
        ServerCallContext context)
    {
        var bot = context.GetBot();

        if (!_pollingGuard.TryEnter(bot.Id))
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "У бота уже есть активный поток получения update'ов"));
        }

        _metrics.Increment("bot_api_update_streams_opened");

        try
        {
            // offset подтверждает (удаляет) update'ы с id < offset
            if (request.Offset > 0)
            {
                using var confirmScope = _scopeFactory.CreateScope();
                await confirmScope.ServiceProvider.GetRequiredService<BotUpdatesStorage>()
                    .Confirm(bot.Id, request.Offset);
            }

            long nextFromId = request.Offset;

            while (!context.CancellationToken.IsCancellationRequested)
            {
                List<Domain.BotUpdate> batch;

                // Отдельный scope на итерацию — DbContext не живёт всё время стрима
                using (var scope = _scopeFactory.CreateScope())
                {
                    batch = await scope.ServiceProvider.GetRequiredService<BotUpdatesStorage>()
                        .GetBacklog(bot.Id, nextFromId, UpdatesBatchSize);
                }

                if (batch.Count == 0)
                {
                    await _notifier.WaitForUpdateAsync(bot.Id, LiveWaitInterval, context.CancellationToken);
                    continue;
                }

                foreach (var update in batch)
                {
                    await responseStream.WriteAsync(UpdateJsonMapper.ToGrpcUpdate(update.Id, update.Payload));
                    nextFromId = update.Id + 1;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Клиент закрыл стрим — штатное завершение
        }
        finally
        {
            _pollingGuard.Exit(bot.Id);
            _logger.LogDebug("Стрим update'ов бота {BotId} закрыт", bot.Id);
        }
    }
}
