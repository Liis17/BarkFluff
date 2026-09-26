using System.Net;
using System.Text;
using System.Text.Json;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Settings;
using Xunit;

namespace BarkFluff.Identity.Tests.Infrastructure;

public sealed class TelegramAuthBotTests
{
    [Fact]
    public async Task SendCode_UsesCopyableHtmlCodeAfterTitleAndBeforeRequestDetails()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var bot = new TelegramAuthBot(http, new TelegramAuthOptions { Enabled = true, BotToken = "test-token" });

        await bot.SendCode(123, "🔐 BarkFluff · <Node>", "012345", "👤 Аккаунт: user & admin\n🧭 Действие: Вход", default);

        Assert.Equal("https://api.telegram.org/bottest-token/sendMessage", handler.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("HTML", body.RootElement.GetProperty("parse_mode").GetString());
        Assert.Equal(
            "<b>🔐 BarkFluff · &lt;Node&gt;</b>\n\n<code>012345</code>\n\n👤 Аккаунт: user &amp; admin\n🧭 Действие: Вход",
            body.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task EditMessage_ReplacesTextAndRemovesInlineKeyboard()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var bot = new TelegramAuthBot(http, new TelegramAuthOptions { Enabled = true, BotToken = "test-token" });

        await bot.EditMessage(123, 456, "✅ Запрос подтверждён.", default);

        Assert.Equal("https://api.telegram.org/bottest-token/editMessageText", handler.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(123, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal(456, body.RootElement.GetProperty("message_id").GetInt64());
        Assert.Equal("✅ Запрос подтверждён.", body.RootElement.GetProperty("text").GetString());
        Assert.Empty(body.RootElement.GetProperty("reply_markup").GetProperty("inline_keyboard").EnumerateArray());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":true}", Encoding.UTF8, "application/json")
            };
        }
    }
}
