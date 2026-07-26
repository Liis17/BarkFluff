using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Messages;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Bots.Services.BotFather;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

namespace BarkFluff.Bots.Consumers;

/// <summary>
/// Второй consumer NewMessageEvent (собственная очередь new-messages-bots-handler, fanout).
/// Пересекает участников чата с реестром ботов (исключая отправителя) и сохраняет update'ы.
/// </summary>
public class NewMessageConsumer : IConsumer<NewMessageEvent>
{
    private readonly BotRegistryCache _registryCache;
    private readonly BotUpdatesStorage _updatesStorage;
    private readonly BotFatherService _botFatherService;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<NewMessageConsumer> _logger;

    public NewMessageConsumer(
        BotRegistryCache registryCache,
        BotUpdatesStorage updatesStorage,
        BotFatherService botFatherService,
        UsersServerApi.UsersServerApiClient usersClient,
        MetricsCollector metrics,
        ILogger<NewMessageConsumer> logger)
    {
        _registryCache = registryCache;
        _updatesStorage = updatesStorage;
        _botFatherService = botFatherService;
        _usersClient = usersClient;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<NewMessageEvent> context)
    {
        _metrics.Increment("new_message_events_consumed");

        var message = Message.Parser.ParseFrom(context.Message.Message);

        var botIds = _registryCache.FilterBotIds(context.Message.ChatMembers)
            .Where(id => id != message.SenderId)
            .ToList();

        if (botIds.Count == 0)
            return;

        // Имя отправителя для payload (в proto Message его нет)
        var sender = await _usersClient.GetByIdAsync(new GetByIdRequest { UserId = message.SenderId });
        var payloadJson = UpdateJsonMapper.ToPayloadJson(
            message,
            context.Message.ChatId,
            message.SenderId,
            sender.User.Username,
            sender.User.FirstName);

        foreach (var botId in botIds)
        {
            var bot = _registryCache.Get(botId);

            switch (bot?.SystemRole)
            {
                case SystemBotRole.BotFather:
                    _metrics.Increment("botfather_messages_received");
                    await _botFatherService.HandleAsync(message, context.Message.ChatId);
                    break;

                case SystemBotRole.LoginNotifier:
                    // Login-notifier только пишет, входящие игнорирует
                    break;

                default:
                    var updateId = await _updatesStorage.Add(botId, payloadJson);
                    // Fan-out сигнал: разбудить long-poll/стрим-waiter'а бота на любом инстансе.
                    await context.Publish(new BotUpdateSignalEvent { BotId = botId });
                    _metrics.Increment("bot_updates_stored");

                    _logger.LogDebug(
                        "Update {UpdateId} сохранён для бота {BotId} (сообщение {MessageId})",
                        updateId, botId, message.Id);
                    break;
            }
        }
    }
}
