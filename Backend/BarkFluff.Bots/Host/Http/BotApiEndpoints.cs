using System.Text.Json.Serialization;

using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MessagesProto = BarkFluff.Proto.Messages;

namespace BarkFluff.Bots.Host.Http;

/// <summary>
/// Bot REST API (HTTP/1.1 на RunSettings:Http1Port, порт 7028).
/// Маршруты /bot/{method}, bot-JWT — в заголовке x-auth-token (штатный XAuth + BotAuthEndpointFilter).
/// </summary>
public static class BotApiEndpoints
{
    private const int DefaultUpdatesLimit = 100;
    private const int MaxLongPollTimeoutSeconds = 50;
    private const long BotStorageQuotaBytes = 1L * 1024 * 1024 * 1024; // 1 ГБ

    public static void MapBotApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/bot")
            .RequireAuthorization(nameof(TokenType.Bot))
            .AddEndpointFilter<BotAuthEndpointFilter>();

        group.MapGet("/getMe", GetMe);
        group.MapPost("/sendMessage", SendMessage);
        group.MapPost("/sendPhoto", (HttpContext ctx, BotCallerContext caller, MessagesProto.MessagesServerApi.MessagesServerApiClient m, FilesServerApi.FilesServerApiClient f)
            => SendFile(ctx, caller, m, f, UploadFileType.MessageAttachmentImage));
        group.MapPost("/sendDocument", (HttpContext ctx, BotCallerContext caller, MessagesProto.MessagesServerApi.MessagesServerApiClient m, FilesServerApi.FilesServerApiClient f)
            => SendFile(ctx, caller, m, f, UploadFileType.MessageAttachmentDocument));
        group.MapGet("/getUpdates", GetUpdates);
        group.MapGet("/getUserInfo", GetUserInfo);
    }

    private static IResult GetMe(BotCallerContext callerContext)
    {
        var bot = callerContext.Bot;
        return BotApiResponse.Ok(new
        {
            id = bot.Id,
            is_bot = true,
            first_name = bot.Name,
            username = bot.Username,
        });
    }

    private static async Task<IResult> SendMessage(
        BotCallerContext callerContext,
        SendMessageBody body,
        MessagesProto.MessagesServerApi.MessagesServerApiClient messagesClient)
    {
        var bot = callerContext.Bot;

        return await ExecuteAsync(async () =>
        {
            var message = await SendViaMessages(messagesClient, bot.Id, body.ChatId, body.UserId, body.Text ?? string.Empty, []);
            return BotApiResponse.Ok(ToMessageResult(message, body.ChatId));
        });
    }

    private static async Task<IResult> SendFile(
        HttpContext context,
        BotCallerContext callerContext,
        MessagesProto.MessagesServerApi.MessagesServerApiClient messagesClient,
        FilesServerApi.FilesServerApiClient filesClient,
        UploadFileType fileType)
    {
        var bot = callerContext.Bot;

        if (!context.Request.HasFormContentType)
            return BotApiResponse.Error(StatusCodes.Status400BadRequest, "Ожидается multipart/form-data");

        var form = await context.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");

        if (file is null || file.Length == 0)
            return BotApiResponse.Error(StatusCodes.Status400BadRequest, "Поле file обязательно");

        var chatId = form["chat_id"].FirstOrDefault();
        var userIdRaw = form["user_id"].FirstOrDefault();
        long? userId = long.TryParse(userIdRaw, out var parsedUserId) ? parsedUserId : null;
        var caption = form["caption"].FirstOrDefault() ?? string.Empty;

        return await ExecuteAsync(async () =>
        {
            // Квота хранилища вложений бота — 1 ГБ
            var storageInfo = await filesClient.GetUserStorageInfoServerAsync(
                new GetUserStorageInfoServerRequest { UserId = bot.Id });

            if (storageInfo.TotalUsedStorage + file.Length > BotStorageQuotaBytes)
                return BotApiResponse.Error(StatusCodes.Status413PayloadTooLarge, "Превышена квота хранилища бота (1 ГБ)");

            byte[] data;
            using (var memory = new MemoryStream())
            {
                await file.CopyToAsync(memory);
                data = memory.ToArray();
            }

            var uploaded = await filesClient.UploadFileServerAsync(new UploadFileServerRequest
            {
                Data = Google.Protobuf.ByteString.CopyFrom(data),
                Filename = file.FileName,
                FileType = fileType,
                OwnerUserId = bot.Id,
            });

            var message = await SendViaMessages(messagesClient, bot.Id, chatId, userId, caption, [uploaded.FileId]);
            return BotApiResponse.Ok(ToMessageResult(message, chatId));
        });
    }

    private static async Task<IResult> GetUpdates(
        HttpContext context,
        BotCallerContext callerContext,
        BotUpdatesStorage updatesStorage,
        BotUpdateNotifier notifier,
        BotPollingGuard pollingGuard,
        long offset = 0,
        int limit = DefaultUpdatesLimit,
        int timeout = 0)
    {
        var bot = callerContext.Bot;

        limit = Math.Clamp(limit, 1, DefaultUpdatesLimit);
        timeout = Math.Clamp(timeout, 0, MaxLongPollTimeoutSeconds);

        if (!pollingGuard.TryEnter(bot.Id))
            return BotApiResponse.Error(StatusCodes.Status409Conflict, "У бота уже есть активный поток получения update'ов");

        try
        {
            if (offset > 0)
                await updatesStorage.Confirm(bot.Id, offset);

            var batch = await updatesStorage.GetBacklog(bot.Id, offset, limit);

            if (batch.Count == 0 && timeout > 0)
            {
                await notifier.WaitForUpdateAsync(bot.Id, TimeSpan.FromSeconds(timeout), context.RequestAborted);
                batch = await updatesStorage.GetBacklog(bot.Id, offset, limit);
            }

            var result = batch.Select(u =>
            {
                var payload = UpdateJsonMapper.ParsePayload(u.Payload);
                return new UpdateResult { UpdateId = u.Id, Message = payload.Message };
            }).ToList();

            return BotApiResponse.Ok(result);
        }
        finally
        {
            pollingGuard.Exit(bot.Id);
        }
    }

    private static async Task<IResult> GetUserInfo(
        UsersServerApi.UsersServerApiClient usersClient,
        long? user_id = null,
        string? username = null)
    {
        return await ExecuteAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(username))
            {
                // Privacy применяет Users
                var response = await usersClient.GetUserByUsernameAsync(new GetUserByUsernameRequest { Username = username });
                return BotApiResponse.Ok(new
                {
                    id = response.Id,
                    username,
                    first_name = response.FirstName,
                    last_name = response.LastName,
                    bio = response.Bio,
                    avatar_url = response.ProfilePicture,
                    is_bot = response.IsBot,
                });
            }

            if (user_id is > 0)
            {
                var response = await usersClient.GetByIdAsync(new GetByIdRequest { UserId = user_id.Value });
                return BotApiResponse.Ok(new
                {
                    id = response.User.Id,
                    username = response.User.Username,
                    first_name = response.User.FirstName,
                    last_name = response.User.LastName,
                    bio = response.User.Bio,
                    avatar_url = response.User.ProfilePicture,
                    is_bot = response.User.IsBot,
                });
            }

            return BotApiResponse.Error(StatusCodes.Status400BadRequest, "user_id или username обязателен");
        });
    }

    // ── Общие помощники ────────────────────────────────────────────────────

    private static async Task<Proto.Shared.Message> SendViaMessages(
        MessagesProto.MessagesServerApi.MessagesServerApiClient messagesClient,
        long botId,
        string? chatId,
        long? userId,
        string text,
        IEnumerable<string> fileIds)
    {
        var request = new MessagesProto.SendMessageServerRequest
        {
            SenderUserId = botId,
            Message = new MessagesProto.OutgoingMessage { Text = text },
        };
        request.Message.FilesIds.AddRange(fileIds);

        if (!string.IsNullOrWhiteSpace(chatId))
            request.ChatId = chatId;
        else if (userId is > 0)
            request.UserId = userId.Value;
        else
            throw new RpcException(new Status(StatusCode.InvalidArgument, "chat_id или user_id обязателен"));

        var response = await messagesClient.SendMessageServerAsync(request);
        return response.Message;
    }

    private static object ToMessageResult(Proto.Shared.Message message, string? chatId) => new
    {
        message_id = message.Id,
        chat_id = chatId ?? string.Empty,
        date = message.SentAt?.Seconds ?? 0,
        text = message.Content?.Text ?? string.Empty,
        attachments = message.Content?.Attachments?.Select(a => new
        {
            file_id = a.FileId,
            type = a.Type.ToString().ToLowerInvariant(),
            preview_url = a.PreviewUrl,
            file_size = a.AttachmentSize,
        }),
    };

    /// <summary>Маппинг ошибок бэкенда в {ok:false,error_code,description}.</summary>
    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BaseGrpcException ex)
        {
            return BotApiResponse.Error(StatusCodes.Status400BadRequest, ex.ErrorMessage);
        }
        catch (RpcException ex)
        {
            var statusCode = ex.StatusCode switch
            {
                StatusCode.InvalidArgument => StatusCodes.Status400BadRequest,
                StatusCode.NotFound => StatusCodes.Status404NotFound,
                StatusCode.PermissionDenied => StatusCodes.Status403Forbidden,
                StatusCode.Unauthenticated => StatusCodes.Status401Unauthorized,
                StatusCode.ResourceExhausted => StatusCodes.Status429TooManyRequests,
                _ => StatusCodes.Status500InternalServerError,
            };

            return BotApiResponse.Error(statusCode, ex.Status.Detail);
        }
    }
}

public class SendMessageBody
{
    [JsonPropertyName("chat_id")]
    public string? ChatId { get; set; }

    [JsonPropertyName("user_id")]
    public long? UserId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public class UpdateResult
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("message")]
    public IncomingMessagePayload Message { get; set; } = new();
}
