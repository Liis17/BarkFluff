using System.Net.Http.Json;
using System.Text.Json;
using BarkFluff.Identity.Settings;
using Grpc.Core;

namespace BarkFluff.Identity.Infrastructure;

public record TelegramBotIdentity(long Id, string Username);

public interface ITelegramAuthBot
{
    Task<TelegramBotIdentity> GetIdentity(CancellationToken ct);
    Task<JsonElement[]> GetUpdates(long offset, CancellationToken ct);
    Task Send(long chatId, string text, string? approvalToken, CancellationToken ct);
    Task SendCode(long chatId, string title, string code, string details, CancellationToken ct);
    Task Answer(string callbackId, string text, CancellationToken ct);
    Task EditMessage(long chatId, long messageId, string text, CancellationToken ct);
}

public sealed class TelegramAuthBot(HttpClient http, TelegramAuthOptions options) : ITelegramAuthBot
{
    private TelegramBotIdentity? _identity;

    private async Task<JsonElement> Call(string method, object body, CancellationToken ct)
    {
        if (!options.Configured) throw new RpcException(new Status(StatusCode.Unavailable, "Telegram is unavailable"));
        try
        {
            using var response = await http.PostAsJsonAsync($"https://api.telegram.org/bot{options.BotToken}/{method}", body, ct);
            if (!response.IsSuccessStatusCode) throw new RpcException(new Status(StatusCode.Unavailable, "Telegram delivery failed"));
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!json.RootElement.GetProperty("ok").GetBoolean())
                throw new RpcException(new Status(StatusCode.Unavailable, "Telegram delivery failed"));
            return json.RootElement.GetProperty("result").Clone();
        }
        catch (HttpRequestException) { throw new RpcException(new Status(StatusCode.Unavailable, "Telegram is unavailable")); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { throw new RpcException(new Status(StatusCode.Unavailable, "Telegram timed out")); }
    }

    public async Task<TelegramBotIdentity> GetIdentity(CancellationToken ct)
    {
        if (_identity != null) return _identity;
        var me = await Call("getMe", new { }, ct);
        return _identity = new TelegramBotIdentity(me.GetProperty("id").GetInt64(), me.GetProperty("username").GetString()!);
    }

    public async Task<JsonElement[]> GetUpdates(long offset, CancellationToken ct)
    {
        var updates = await Call("getUpdates", new { offset, timeout = 25, allowed_updates = new[] { "message", "callback_query" } }, ct);
        return updates.EnumerateArray().Select(x => x.Clone()).ToArray();
    }

    public async Task Send(long chatId, string text, string? approvalToken, CancellationToken ct)
    {
        if (approvalToken == null)
            await Call("sendMessage", new { chat_id = chatId, text }, ct);
        else
            await Call("sendMessage", new { chat_id = chatId, text, reply_markup = new { inline_keyboard = new[] {
                new[] { new { text = "✅ Подтвердить", callback_data = "yes:" + approvalToken }, new { text = "❌ Отклонить", callback_data = "no:" + approvalToken } }
            } } }, ct);
    }

    public async Task SendCode(long chatId, string title, string code, string details, CancellationToken ct)
    {
        var text = $"<b>{EscapeHtml(title)}</b>\n\n<code>{EscapeHtml(code)}</code>\n\n{EscapeHtml(details)}";
        await Call("sendMessage", new { chat_id = chatId, text, parse_mode = "HTML" }, ct);
    }

    private static string EscapeHtml(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public async Task Answer(string callbackId, string text, CancellationToken ct) =>
        _ = await Call("answerCallbackQuery", new { callback_query_id = callbackId, text }, ct);

    public async Task EditMessage(long chatId, long messageId, string text, CancellationToken ct) =>
        _ = await Call("editMessageText", new
        {
            chat_id = chatId,
            message_id = messageId,
            text,
            reply_markup = new { inline_keyboard = Array.Empty<object[]>() }
        }, ct);
}
