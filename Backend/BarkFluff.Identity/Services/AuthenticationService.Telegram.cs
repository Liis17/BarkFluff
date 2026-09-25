using System.Text.Json;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Security;
using BarkFluff.Proto.Identity;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Services;

public sealed partial class AuthenticationService
{
    public async Task ProcessTelegramUpdate(JsonElement update, CancellationToken ct)
    {
        if (!telegram.Configured) return;
        var isCallback = update.TryGetProperty("callback_query", out var callback);
        var message = isCallback && callback.TryGetProperty("message", out var callbackMessage)
            ? callbackMessage : update.TryGetProperty("message", out var ordinaryMessage) ? ordinaryMessage : default;
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("chat", out var chat) ||
            chat.GetProperty("type").GetString() != "private") return;
        var from = isCallback ? callback.GetProperty("from") : message.GetProperty("from");
        if (from.TryGetProperty("is_bot", out var isBot) && isBot.GetBoolean()) return;
        var actor = from.GetProperty("id").GetInt64();
        if (chat.GetProperty("id").GetInt64() != actor) return;
        var text = isCallback ? callback.GetProperty("data").GetString() ?? "" :
            message.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? "" : "";
        var approve = text.StartsWith("yes:", StringComparison.Ordinal);
        var reject = text.StartsWith("no:", StringComparison.Ordinal);
        var start = !isCallback && text.StartsWith("/start ", StringComparison.Ordinal);
        if (!(start || (isCallback && (approve || reject)))) return;
        var token = start ? text[7..].Trim() : text[(approve ? 4 : 3)..];
        if (token.Length != 48) return;
        var hash = secrets.Hash(token);
        var initial = await db.AuthenticationChallenges.AsNoTracking().SingleOrDefaultAsync(x => x.TelegramTokenHash == hash, ct);
        if (initial == null)
        {
            if (isCallback) await bot.Answer(callback.GetProperty("id").GetString()!, "Запрос уже обработан или истёк", ct);
            return;
        }
        var key = initial.UserId == 0 ? BitConverter.ToInt64(initial.Id.ToByteArray()) : initial.UserId;
        var accepted = await store.ForUser(key, async () =>
        {
            var c = await db.AuthenticationChallenges.SingleOrDefaultAsync(x => x.Id == initial.Id, ct);
            if (c == null || c.TelegramTokenHash != hash) return false;
            await RefreshState(c, ct);
            if (c.State != AuthChallengeState.Waiting || (c.TelegramId.HasValue && c.TelegramId != actor)) return false;
            if (start)
            {
                if (c.Purpose is not (AuthenticationPurpose.Registration or AuthenticationPurpose.TelegramBinding)) return false;
                if (await db.AuthUserProperties.AnyAsync(x => x.TelegramId == actor && x.UserId != c.UserId, ct)) return false;
                c.TelegramId = actor;
                c.TelegramUsername = from.TryGetProperty("username", out var username) ? username.GetString() : null;
                // Opening a link is not approval; the callback has a new secret unknown to the browser.
                var approvalToken = AuthenticationSecrets.NewSecret();
                c.TelegramTokenHash = secrets.Hash(approvalToken);
                await bot.Send(actor, OperationText(c), approvalToken, ct);
            }
            else
            {
                if (c.TelegramId != actor) return false;
                c.State = approve ? AuthChallengeState.Approved : AuthChallengeState.Rejected;
                c.TelegramTokenHash = null;
            }
            return true;
        }, ct);
        if (isCallback)
            await bot.Answer(callback.GetProperty("id").GetString()!, accepted ? (approve ? "Подтверждено" : "Отклонено") : "Запрос недействителен", ct);
    }
}
