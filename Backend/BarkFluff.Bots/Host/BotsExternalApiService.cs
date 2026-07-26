using BarkFluff.Bots.Features.GetBotFile;
using BarkFluff.Bots.Features.GetBotUserInfo;
using BarkFluff.Bots.Features.GetMe;
using BarkFluff.Bots.Features.SendBotMessage;
using BarkFluff.Bots.Mapping;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Bots;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Bots.Host;

/// <summary>
/// Внешний Bot API (gRPC). Аутентификация — bot-JWT в заголовке x-auth-token (штатный XAuth),
/// сверка token-id + rate-limit — BotAuthInterceptor (вешается через AddServiceOptions только на этот сервис).
/// Унарные методы — тонкий маппинг + mediator; SubscribeUpdates остаётся в Host (server-streaming в MediatR не ложится).
/// </summary>
[Authorize(Policy = nameof(TokenType.Bot))]
public class BotsExternalApiService : BotsExternalApi.BotsExternalApiBase
{
    private const int UpdatesBatchSize = 100;
    private static readonly TimeSpan LiveWaitInterval = TimeSpan.FromSeconds(25);

    private readonly IMediator _mediator;
    private readonly BotCallerContext _callerContext;
    private readonly BotUpdateNotifier _notifier;
    private readonly IBotPollingGuard _pollingGuard;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<BotsExternalApiService> _logger;

    public BotsExternalApiService(
        IMediator mediator,
        BotCallerContext callerContext,
        BotUpdateNotifier notifier,
        IBotPollingGuard pollingGuard,
        IServiceScopeFactory scopeFactory,
        MetricsCollector metrics,
        ILogger<BotsExternalApiService> logger)
    {
        _mediator = mediator;
        _callerContext = callerContext;
        _notifier = notifier;
        _pollingGuard = pollingGuard;
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _logger = logger;
    }

    public override Task<GetMeResponse> GetMe(GetMeRequest request, ServerCallContext context)
        => _mediator.Send(new GetMeQuery { BotId = _callerContext.Bot.Id }, context.CancellationToken);

    public override async Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context)
    {
        var chatId = request.TargetCase == SendMessageRequest.TargetOneofCase.ChatId ? request.ChatId : null;

        var message = await _mediator.Send(new SendBotMessageCommand
        {
            BotId = _callerContext.Bot.Id,
            ChatId = chatId,
            UserId = request.TargetCase == SendMessageRequest.TargetOneofCase.UserId ? request.UserId : null,
            Text = request.Text ?? string.Empty,
            FileIds = request.FileIds.ToList(),
        }, context.CancellationToken);

        return message.ToSendMessageResponse(chatId ?? string.Empty);
    }

    public override Task<GetUserInfoResponse> GetUserInfo(GetUserInfoRequest request, ServerCallContext context)
        => _mediator.Send(new GetBotUserInfoQuery
        {
            UserId = request.UserCase == GetUserInfoRequest.UserOneofCase.UserId ? request.UserId : null,
            Username = request.UserCase == GetUserInfoRequest.UserOneofCase.Username ? request.Username : null,
        }, context.CancellationToken);

    public override Task<GetFileResponse> GetFile(GetFileRequest request, ServerCallContext context)
        => _mediator.Send(new GetBotFileQuery { FileId = request.FileId }, context.CancellationToken);

    public override async Task SubscribeUpdates(
        SubscribeUpdatesRequest request,
        IServerStreamWriter<BotUpdate> responseStream,
        ServerCallContext context)
    {
        var bot = _callerContext.Bot;

        if (!await _pollingGuard.TryEnterAsync(bot.Id))
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
                // Продлеваем распределённый лок — долгоживущий стрим держит слот дольше его TTL.
                await _pollingGuard.RenewAsync(bot.Id);

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
            await _pollingGuard.ExitAsync(bot.Id);
            _logger.LogDebug("Стрим update'ов бота {BotId} закрыт", bot.Id);
        }
    }
}
