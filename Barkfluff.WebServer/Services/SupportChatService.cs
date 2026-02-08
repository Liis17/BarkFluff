using System.Collections.Concurrent;

namespace Barkfluff.WebServer.Services
{
    public class SupportChatService
    {
        private readonly ConcurrentDictionary<string, ChatSession> _chats = new();
        private readonly ConcurrentDictionary<int, string> _telegramMessageMap = new();

        public void AddMessage(string chatId, string text, bool isAdmin)
        {
            var chat = _chats.GetOrAdd(chatId, _ => new ChatSession { ChatId = chatId });
            lock (chat.Lock)
            {
                chat.Messages.Add(new ChatMessage
                {
                    Text = text,
                    IsAdmin = isAdmin,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        public List<ChatMessage> GetMessages(string chatId, int afterIndex = 0)
        {
            if (_chats.TryGetValue(chatId, out var chat))
            {
                lock (chat.Lock)
                {
                    return chat.Messages.Skip(afterIndex).ToList();
                }
            }
            return new List<ChatMessage>();
        }

        public int GetMessageCount(string chatId)
        {
            if (_chats.TryGetValue(chatId, out var chat))
            {
                lock (chat.Lock)
                {
                    return chat.Messages.Count;
                }
            }
            return 0;
        }

        public bool ChatExists(string chatId) => _chats.ContainsKey(chatId);

        public void TrackTelegramMessage(int telegramMessageId, string chatId)
        {
            _telegramMessageMap[telegramMessageId] = chatId;
        }

        public string? GetChatIdByTelegramMessage(int telegramMessageId)
        {
            return _telegramMessageMap.TryGetValue(telegramMessageId, out var chatId) ? chatId : null;
        }
    }

    public class ChatSession
    {
        public string ChatId { get; set; } = string.Empty;
        public List<ChatMessage> Messages { get; set; } = new();
        public object Lock { get; } = new();
    }

    public class ChatMessage
    {
        public string Text { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
