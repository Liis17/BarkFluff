using System.Text.Json.Serialization;

using BarkFluff.Bots.Features.DeleteBotMessage;
using BarkFluff.Bots.Features.EditBotMessage;
using BarkFluff.Bots.Features.GetBotFile;
using BarkFluff.Bots.Features.GetBotUpdates;
using BarkFluff.Bots.Features.GetBotUserInfo;
using BarkFluff.Bots.Features.GetMe;
using BarkFluff.Bots.Features.GetMyCommands;
using BarkFluff.Bots.Features.SetMyCommands;
using BarkFluff.Bots.Features.SendBotFile;
using BarkFluff.Bots.Features.SendBotMessage;
using BarkFluff.Bots.Mapping;
using BarkFluff.Bots.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Exceptions;
using BarkFluff.Shared.Exceptions.Bots;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Bots.Host.Http;

/// <summary>
/// Bot REST API (HTTP/1.1 на RunSettings:Http1Port, порт 7028).
/// Маршруты /bot/{method}, bot-JWT — в заголовке x-auth-token (штатный XAuth + BotAuthEndpointFilter).
/// Endpoints — тонкий маппинг + mediator; multipart-парсинг остаётся здесь.
/// </summary>
public static class BotApiEndpoints
{
    private const int DefaultUpdatesLimit = 100;

    public static void MapBotApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/bot")
            .RequireAuthorization(nameof(TokenType.Bot))
            .AddEndpointFilter<BotAuthEndpointFilter>();

        group.MapGet("/getMe", GetMe);
        group.MapPost("/sendMessage", SendMessage);
        group.MapPost("/sendPhoto", (HttpContext ctx, BotCallerContext caller, IMediator mediator)
            => SendFile(ctx, caller, mediator, UploadFileType.MessageAttachmentImage));
        group.MapPost("/sendDocument", (HttpContext ctx, BotCallerContext caller, IMediator mediator)
            => SendFile(ctx, caller, mediator, UploadFileType.MessageAttachmentDocument));
        group.MapGet("/getUpdates", GetUpdates);
        group.MapGet("/getUserInfo", GetUserInfo);
        group.MapGet("/getFile", GetFile);
        group.MapPost("/editMessage", EditMessage);
        group.MapPost("/deleteMessage", DeleteMessage);
        group.MapPost("/setMyCommands", SetMyCommands);
        group.MapGet("/getMyCommands", GetMyCommands);
    }

    private static async Task<IResult> GetMe(BotCallerContext callerContext, IMediator mediator)
    {
        var botId = callerContext.Bot.Id;

        return await ExecuteAsync(async () =>
        {
            var response = await mediator.Send(new GetMeQuery { BotId = botId });
            return BotApiResponse.Ok(response.ToHttpResult());
        });
    }

    private static async Task<IResult> SendMessage(
        BotCallerContext callerContext,
        SendMessageBody body,
        IMediator mediator)
    {
        var botId = callerContext.Bot.Id;

        return await ExecuteAsync(async () =>
        {
            var message = await mediator.Send(new SendBotMessageCommand
            {
                BotId = botId,
                ChatId = body.ChatId,
                UserId = body.UserId,
                Text = body.Text ?? string.Empty,
            });

            return BotApiResponse.Ok(message.ToHttpMessageResult(body.ChatId));
        });
    }

    private static async Task<IResult> SendFile(
        HttpContext context,
        BotCallerContext callerContext,
        IMediator mediator,
        UploadFileType fileType)
    {
        var botId = callerContext.Bot.Id;

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

        byte[] data;
        using (var memory = new MemoryStream())
        {
            await file.CopyToAsync(memory);
            data = memory.ToArray();
        }

        return await ExecuteAsync(async () =>
        {
            var message = await mediator.Send(new SendBotFileCommand
            {
                BotId = botId,
                ChatId = chatId,
                UserId = userId,
                Caption = caption,
                FileName = file.FileName,
                Data = data,
                FileType = fileType,
            });

            return BotApiResponse.Ok(message.ToHttpMessageResult(chatId));
        });
    }

    private static async Task<IResult> GetUpdates(
        HttpContext context,
        BotCallerContext callerContext,
        IMediator mediator,
        long offset = 0,
        int limit = DefaultUpdatesLimit,
        int timeout = 0)
    {
        var botId = callerContext.Bot.Id;

        return await ExecuteAsync(async () =>
        {
            var batch = await mediator.Send(new GetBotUpdatesQuery
            {
                BotId = botId,
                Offset = offset,
                Limit = limit,
                TimeoutSeconds = timeout,
            }, context.RequestAborted);

            return BotApiResponse.Ok(batch.Select(u => u.ToUpdateResult()).ToList());
        });
    }

    private static async Task<IResult> GetUserInfo(
        IMediator mediator,
        long? user_id = null,
        string? username = null)
    {
        return await ExecuteAsync(async () =>
        {
            var response = await mediator.Send(new GetBotUserInfoQuery { UserId = user_id, Username = username });
            return BotApiResponse.Ok(response.ToHttpResult());
        });
    }

    private static async Task<IResult> EditMessage(
        BotCallerContext callerContext,
        EditMessageBody body,
        IMediator mediator)
    {
        var botId = callerContext.Bot.Id;

        return await ExecuteAsync(async () =>
        {
            var message = await mediator.Send(new EditBotMessageCommand
            {
                BotId = botId,
                MessageId = body.MessageId,
                Text = body.Text ?? string.Empty,
                FileIds = body.FileIds ?? [],
            });

            return BotApiResponse.Ok(message.ToHttpEditResult());
        });
    }

    private static async Task<IResult> DeleteMessage(
        BotCallerContext callerContext,
        DeleteMessageBody body,
        IMediator mediator)
    {
        var botId = callerContext.Bot.Id;

        return await ExecuteAsync(async () =>
        {
            await mediator.Send(new DeleteBotMessageCommand { BotId = botId, MessageId = body.MessageId });
            return BotApiResponse.Ok(true);
        });
    }

    private static async Task<IResult> GetFile(IMediator mediator, string? file_id = null)
    {
        return await ExecuteAsync(async () =>
        {
            var response = await mediator.Send(new GetBotFileQuery { FileId = file_id ?? string.Empty });
            return BotApiResponse.Ok(response.ToHttpResult());
        });
    }

    private static async Task<IResult> SetMyCommands(
        BotCallerContext callerContext,
        SetMyCommandsBody body,
        IMediator mediator)
    {
        var botId = callerContext.Bot.Id;

        return await ExecuteAsync(async () =>
        {
            await mediator.Send(new SetMyCommandsCommand
            {
                BotId = botId,
                Commands = (body.Commands ?? []).Select(c => new Domain.BotCommand
                {
                    Command = c.Command ?? string.Empty,
                    Description = c.Description ?? string.Empty,
                }).ToList(),
            });

            return BotApiResponse.Ok(true);
        });
    }

    private static async Task<IResult> GetMyCommands(BotCallerContext callerContext, IMediator mediator)
    {
        var botId = callerContext.Bot.Id;

        return await ExecuteAsync(async () =>
        {
            var response = await mediator.Send(new GetMyCommandsQuery { BotId = botId });
            return BotApiResponse.Ok(response.ToHttpResult());
        });
    }

    /// <summary>Маппинг ошибок бэкенда в {ok:false,error_code,description}.</summary>
    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BotPollingConflictException ex)
        {
            return BotApiResponse.Error(StatusCodes.Status409Conflict, ex.ErrorMessage);
        }
        catch (BotStorageQuotaExceededException ex)
        {
            return BotApiResponse.Error(StatusCodes.Status413PayloadTooLarge, ex.ErrorMessage);
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

public class EditMessageBody
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Заменяет вложения целиком; не передан — вложения будут сняты.</summary>
    [JsonPropertyName("file_ids")]
    public List<string>? FileIds { get; set; }
}

public class DeleteMessageBody
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }
}

public class SetMyCommandsBody
{
    [JsonPropertyName("commands")]
    public List<BotCommandBody>? Commands { get; set; }
}

public class BotCommandBody
{
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class UpdateResult
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("message")]
    public IncomingMessagePayload Message { get; set; } = new();
}
