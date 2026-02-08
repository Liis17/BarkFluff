using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Barkfluff.WebServer.Services
{
    public class TelegramService
    {
        private readonly TelegramBotClient? _bot;
        private readonly SupportChatService _chatService;
        private readonly ILogger<TelegramService> _logger;
        private readonly long _adminId = 495716470;
        private readonly bool _isConfigured;

        public TelegramService(string? token, SupportChatService chatService, ILogger<TelegramService> logger)
        {
            _chatService = chatService;
            _logger = logger;
            _isConfigured = !string.IsNullOrEmpty(token);

            if (_isConfigured)
            {
                _bot = new TelegramBotClient(token!);
            }
        }

        public void Start()
        {
            if (!_isConfigured || _bot == null)
            {
                _logger.LogWarning("Telegram bot token not configured, support chat Telegram integration disabled");
                return;
            }

            _bot.OnMessage += HandleMessage;
            _bot.OnError += HandleError;
            _logger.LogInformation("Telegram support bot started");
        }

        private Task HandleMessage(Message message, UpdateType type)
        {
            try
            {
                // Only process messages from admin
                if (message.Chat.Id != _adminId)
                    return Task.CompletedTask;

                // Admin must reply to a bot message to respond to a support chat
                if (message.ReplyToMessage == null || string.IsNullOrEmpty(message.Text))
                    return Task.CompletedTask;

                var originalText = message.ReplyToMessage.Text;
                if (string.IsNullOrEmpty(originalText))
                    return Task.CompletedTask;

                // First line of the original message is the chat GUID
                var firstLine = originalText.Split('\n')[0].Trim();

                if (Guid.TryParse(firstLine, out _) && _chatService.ChatExists(firstLine))
                {
                    _chatService.AddMessage(firstLine, message.Text, isAdmin: true);
                    _logger.LogInformation("Admin replied to support chat {ChatId}", firstLine);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling Telegram message");
            }

            return Task.CompletedTask;
        }

        public async Task SendToAdmin(string chatId, string userMessage)
        {
            if (!_isConfigured || _bot == null)
                return;

            var text = $"{chatId}\n\n{userMessage}";
            var sent = await _bot.SendMessage(_adminId, text);
            _chatService.TrackTelegramMessage(sent.Id, chatId);
        }

        private Task HandleError(Exception ex, HandleErrorSource source)
        {
            _logger.LogError(ex, "Telegram bot error ({Source})", source);
            return Task.CompletedTask;
        }
    }
}
