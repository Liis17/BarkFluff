using System.Text.RegularExpressions;

using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;

using Google.Protobuf.Collections;

using MessagesProto = BarkFluff.Proto.Messages;

namespace BarkFluff.Bots.Services.BotFather;

/// <summary>
/// State machine диалога @botfather: создание и управление ботами из чата.
/// Работает in-process (системный бот), отвечает через SendMessageServer.
/// </summary>
public class BotFatherService
{
    private static readonly Regex UsernamePattern = new("^[a-zA-Z0-9_]{3,32}$", RegexOptions.Compiled);

    private const string HelpText =
        "Я помогу создать бота и управлять им.\n\n" +
        "/newbot — создать нового бота\n" +
        "/mybots — список твоих ботов\n" +
        "/token <username> — перегенерировать токен\n" +
        "/setname <username> — изменить имя\n" +
        "/setdescription <username> — изменить описание\n" +
        "/setuserpic <username> — изменить аватарку\n" +
        "/deletebot <username> — удалить бота\n" +
        "/cancel — отменить текущую операцию";

    private readonly BotFatherSessionsStorage _sessionsStorage;
    private readonly BotsStorage _botsStorage;
    private readonly BotRegistryCache _registryCache;
    private readonly BotTokenService _tokenService;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly FilesServerApi.FilesServerApiClient _filesClient;
    private readonly MessagesProto.MessagesServerApi.MessagesServerApiClient _messagesClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BotFatherService> _logger;

    public BotFatherService(
        BotFatherSessionsStorage sessionsStorage,
        BotsStorage botsStorage,
        BotRegistryCache registryCache,
        BotTokenService tokenService,
        UsersServerApi.UsersServerApiClient usersClient,
        FilesServerApi.FilesServerApiClient filesClient,
        MessagesProto.MessagesServerApi.MessagesServerApiClient messagesClient,
        IHttpClientFactory httpClientFactory,
        ILogger<BotFatherService> logger)
    {
        _sessionsStorage = sessionsStorage;
        _botsStorage = botsStorage;
        _registryCache = registryCache;
        _tokenService = tokenService;
        _usersClient = usersClient;
        _filesClient = filesClient;
        _messagesClient = messagesClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task HandleAsync(Message message, Guid chatId)
    {
        var userId = message.SenderId;
        var text = message.Content?.Text?.Trim() ?? string.Empty;

        var session = await _sessionsStorage.GetOrCreate(userId);
        string reply;

        try
        {
            reply = text.StartsWith('/')
                ? await HandleCommand(session, text)
                : await HandleStateInput(session, text, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BotFather: ошибка обработки сообщения от {UserId}", userId);
            reply = "Что-то пошло не так. Попробуй ещё раз или /cancel.";
        }

        await _sessionsStorage.Save(session);
        await Reply(chatId, reply);
    }

    // ── Команды ──────────────────────────────────────────────────────────

    private async Task<string> HandleCommand(BotFatherSession session, string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1].Trim().TrimStart('@') : null;

        switch (command)
        {
            case "/start":
            case "/help":
                Reset(session);
                return HelpText;

            case "/cancel":
                var wasIdle = session.State == (int)BotFatherState.Idle;
                Reset(session);
                return wasIdle ? "Нечего отменять." : "Отменено.";

            case "/newbot":
                Reset(session);
                session.State = (int)BotFatherState.AwaitingBotName;
                return "Как назовём бота? Введи отображаемое имя.";

            case "/mybots":
            {
                Reset(session);
                var bots = await _botsStorage.GetByOwner(session.UserId);
                return bots.Count == 0
                    ? "У тебя пока нет ботов. Создай первого: /newbot"
                    : "Твои боты:\n" + string.Join("\n", bots.Select(b => $"@{b.Username} — {b.Name}"));
            }

            case "/token":
            {
                Reset(session);
                var bot = await GetOwnedBot(session.UserId, argument);
                if (bot is null)
                    return OwnedBotNotFoundText(command);

                var (token, tokenHash) = _tokenService.GenerateToken(bot.Id);
                bot.TokenHash = tokenHash;
                await _botsStorage.Update(bot);
                _registryCache.Set(bot);

                return $"Новый токен бота @{bot.Username} (старый отозван):\n\n{token}\n\nСохрани его — он показывается один раз.";
            }

            case "/setname":
                return await BeginContextOperation(session, argument, command,
                    BotFatherState.AwaitingNewName, bot => $"Введи новое имя для @{bot.Username}.");

            case "/setdescription":
                return await BeginContextOperation(session, argument, command,
                    BotFatherState.AwaitingDescription, bot => $"Введи новое описание для @{bot.Username}.");

            case "/setuserpic":
                return await BeginContextOperation(session, argument, command,
                    BotFatherState.AwaitingUserpic, bot => $"Пришли картинку для аватарки @{bot.Username}.");

            case "/deletebot":
                return await BeginContextOperation(session, argument, command,
                    BotFatherState.AwaitingDeleteConfirmation,
                    bot => $"Удалить @{bot.Username}? Это отзовёт токен, вернуть бота нельзя. Напиши «да» для подтверждения.");

            default:
                return "Не знаю такую команду. /help — список команд.";
        }
    }

    private async Task<string> BeginContextOperation(
        BotFatherSession session, string? argument, string command,
        BotFatherState nextState, Func<Bot, string> prompt)
    {
        Reset(session);

        var bot = await GetOwnedBot(session.UserId, argument);
        if (bot is null)
            return OwnedBotNotFoundText(command);

        session.State = (int)nextState;
        session.ContextBotId = bot.Id;
        return prompt(bot);
    }

    // ── Ввод по состоянию ────────────────────────────────────────────────

    private async Task<string> HandleStateInput(BotFatherSession session, string text, Message message)
    {
        switch ((BotFatherState)session.State)
        {
            case BotFatherState.AwaitingBotName:
                if (string.IsNullOrWhiteSpace(text))
                    return "Имя не может быть пустым. Введи отображаемое имя бота.";

                session.PendingName = text;
                session.State = (int)BotFatherState.AwaitingBotUsername;
                return "Теперь придумай username: латиница, цифры и подчёркивания, 3–32 символа, обязательно заканчивается на «bot». Например: my_cool_bot";

            case BotFatherState.AwaitingBotUsername:
                return await CreateBot(session, text);

            case BotFatherState.AwaitingNewName:
            {
                var bot = await GetContextBot(session);
                if (bot is null)
                    return SessionLostText(session);

                if (string.IsNullOrWhiteSpace(text))
                    return "Имя не может быть пустым.";

                await _usersClient.UpdateProfileServerAsync(new UpdateProfileServerRequest
                {
                    UserId = bot.Id,
                    FirstName = text,
                });

                bot.Name = text;
                await _botsStorage.Update(bot);
                _registryCache.Set(bot);

                Reset(session);
                return $"Имя @{bot.Username} обновлено: {text}";
            }

            case BotFatherState.AwaitingDescription:
            {
                var bot = await GetContextBot(session);
                if (bot is null)
                    return SessionLostText(session);

                await _usersClient.UpdateProfileServerAsync(new UpdateProfileServerRequest
                {
                    UserId = bot.Id,
                    Bio = text,
                });

                Reset(session);
                return $"Описание @{bot.Username} обновлено.";
            }

            case BotFatherState.AwaitingUserpic:
            {
                var bot = await GetContextBot(session);
                if (bot is null)
                    return SessionLostText(session);

                var result = await SetUserpic(bot, message.Content?.Attachments);
                if (result is not null)
                    return result; // ошибка — состояние сохраняем, юзер может прислать другую картинку

                Reset(session);
                return $"Аватарка @{bot.Username} обновлена.";
            }

            case BotFatherState.AwaitingDeleteConfirmation:
            {
                var bot = await GetContextBot(session);
                if (bot is null)
                    return SessionLostText(session);

                if (!text.Equals("да", StringComparison.OrdinalIgnoreCase) &&
                    !text.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    Reset(session);
                    return "Удаление отменено.";
                }

                await _usersClient.DeleteBotUserAsync(new DeleteBotUserRequest { UserId = bot.Id });
                await _botsStorage.Delete(bot.Id);
                _registryCache.Remove(bot.Id);

                Reset(session);
                return $"Бот @{bot.Username} удалён, токен отозван.";
            }

            default:
                return "Не понимаю. /help — список команд.";
        }
    }

    private async Task<string> CreateBot(BotFatherSession session, string username)
    {
        username = username.Trim().TrimStart('@');

        if (!UsernamePattern.IsMatch(username) || !username.EndsWith("bot", StringComparison.OrdinalIgnoreCase))
            return "Невалидный username: латиница, цифры и подчёркивания, 3–32 символа, суффикс «bot». Попробуй ещё раз.";

        var createResponse = await _usersClient.CreateBotUserAsync(new CreateBotUserRequest
        {
            Username = username,
            FirstName = session.PendingName ?? username,
            BypassUsernameRules = false,
        });

        if (createResponse.AlreadyExisted)
            return $"Username @{username} уже занят. Придумай другой.";

        var (token, tokenHash) = _tokenService.GenerateToken(createResponse.UserId);

        var bot = new Bot
        {
            Id = createResponse.UserId,
            OwnerUserId = session.UserId,
            Username = username,
            Name = session.PendingName ?? username,
            TokenHash = tokenHash,
            SystemRole = SystemBotRole.None,
            CreatedAt = DateTime.UtcNow,
        };

        await _botsStorage.Add(bot);
        _registryCache.Set(bot);

        Reset(session);

        _logger.LogInformation("BotFather: пользователь {UserId} создал бота @{Username} (id {BotId})",
            session.UserId, username, bot.Id);

        return $"Готово! Бот @{username} создан.\n\nТокен:\n{token}\n\n" +
               "Сохрани его — он показывается один раз. Перегенерация: /token " + username;
    }

    private async Task<string?> SetUserpic(Bot bot, RepeatedField<MessageAttachment>? attachments)
    {
        var image = attachments?.FirstOrDefault(a => a.Type == MessageAttachmentType.Image);
        if (image is null)
            return "Нужна картинка. Пришли изображение (не документом).";

        // Скачиваем оригинал вложения и загружаем как аватар бота
        var fileData = await _filesClient.GetFileDataAsync(new GetFileDataRequest { FileId = image.FileId });

        using var httpClient = _httpClientFactory.CreateClient();
        var imageBytes = await httpClient.GetByteArrayAsync(fileData.FileInfo.FileUrl);

        var uploaded = await _filesClient.UploadAvatarServerAsync(new UploadAvatarServerRequest
        {
            ImageData = Google.Protobuf.ByteString.CopyFrom(imageBytes),
            Filename = fileData.FileInfo.FileName,
            UserId = bot.Id,
        });

        await _usersClient.SetProfilePictureServerAsync(new SetProfilePictureServerRequest
        {
            UserId = bot.Id,
            ProfilePictureUrl = uploaded.FileUrl,
            ProfilePicturePreviewUrl = uploaded.PreviewUrl,
        });

        return null;
    }

    // ── Помощники ────────────────────────────────────────────────────────

    private async Task<Bot?> GetOwnedBot(long userId, string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var bot = await _botsStorage.GetByUsername(username);
        return bot?.OwnerUserId == userId ? bot : null;
    }

    private async Task<Bot?> GetContextBot(BotFatherSession session)
    {
        if (session.ContextBotId is not { } botId)
            return null;

        var bot = await _botsStorage.GetById(botId);
        return bot?.OwnerUserId == session.UserId ? bot : null;
    }

    private static string OwnedBotNotFoundText(string command)
        => $"Укажи username своего бота: {command} my_cool_bot. Список ботов: /mybots";

    private static string SessionLostText(BotFatherSession session)
    {
        Reset(session);
        return "Бот не найден — операция отменена. /help";
    }

    private static void Reset(BotFatherSession session)
    {
        session.State = (int)BotFatherState.Idle;
        session.ContextBotId = null;
        session.PendingName = null;
    }

    private async Task Reply(Guid chatId, string text)
    {
        var botFather = _registryCache.GetBySystemRole(SystemBotRole.BotFather);
        if (botFather is null)
        {
            _logger.LogError("BotFather не найден в реестре — ответ не отправлен");
            return;
        }

        await _messagesClient.SendMessageServerAsync(new MessagesProto.SendMessageServerRequest
        {
            SenderUserId = botFather.Id,
            ChatId = chatId.ToString(),
            Message = new MessagesProto.OutgoingMessage { Text = text },
        });
    }
}
