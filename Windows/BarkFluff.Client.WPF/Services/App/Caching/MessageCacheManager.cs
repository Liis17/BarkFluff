using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using LiteDB;

using System.IO;
using System.Net.Http;

namespace BarkFluff.Client.WPF.Services.App.Caching
{
    public enum MessageOperation
    {
        Added,
        Deleted,
        Edited
    }
    public class MessageCacheManager : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _fileCacheDir;
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<MessageModel> _messages;
        private readonly ILiteCollection<CachedFile> _files;
        private readonly ILiteCollection<ChatCacheClass> _chats;
        private readonly object _lock = new object();
        private readonly HttpClient _httpClient = new HttpClient();

        public event Action<string, string> FileCached;
        public event Action<string, MessageModel> ChatListUpdated;
        public event Action<MessageModel> ActiveChatUpdated;

        public MessageCacheManager(string dbPath, string fileCacheDir)
        {
            _dbPath = dbPath;
            _fileCacheDir = fileCacheDir;
            //Directory.CreateDirectory(fileCacheDir);
            _db = new LiteDatabase(_dbPath);
            _messages = _db.GetCollection<MessageModel>("messages");
            _files = _db.GetCollection<CachedFile>("files");
            _chats = _db.GetCollection<ChatCacheClass>("chats");
            _messages.EnsureIndex(x => x.ChatId);
            _messages.EnsureIndex(x => x.MessageId);
            _files.EnsureIndex(x => x.Hash);
            _chats.EnsureIndex(x => x.ChatId);
        }

        public string GetCachedFilePath(string fileId, string? providedUrl = null)
        {
            lock (_lock)
            {
                var cached = _files.FindOne(x => x.Hash == fileId);
                if (cached != null)
                {
                    return cached.Path;
                }
            }

            string placeholder = "pack://application:,,,/BarkFluff;component/Resources/Placeholders/userplaceholder.png";

            Task.Run(async () =>
            {
                var response = await BarkFluff.Client.WPF.App.ServerCommunication.GetFile(BarkFluff.Client.WPF.App.GParam, fileId);
                if (!response.error.IsSuccess)
                {
                    WPF.App.ErideMessage.AddMessage($"Не удалось загрузить файл {fileId}: {response.error.ErrorMessage}", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                    return;
                }
                string url = !string.IsNullOrEmpty(providedUrl)
                    ? providedUrl
                    : response.url;

                string extension = Path.GetExtension(new Uri(url).AbsolutePath) ?? ".png";
                string filePath = Path.Combine(_fileCacheDir, $"{fileId}{extension}");

                var bytes = await _httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, bytes);

                lock (_lock)
                {
                    _files.Insert(new CachedFile { Hash = fileId, Path = filePath });
                }

                FileCached?.Invoke(fileId, filePath);
            });

            return placeholder;
        }

        public List<MessageModel> GetMessages(string chatId, long fromMessageId, int offset)
        {
            lock (_lock)
            {
                var query = _messages.Find(x => x.ChatId == chatId);
                if (offset > 0)
                {
                    // В прошлое: сообщения старше fromMessageId
                    var fromMessage = _messages.FindOne(x => x.ChatId == chatId && x.MessageId == fromMessageId);
                    if (fromMessage == null) return new List<MessageModel>();
                    var timestamp = fromMessage.SentAt;
                    return query.Where(x => x.SentAt < timestamp)
                                .OrderByDescending(x => x.SentAt)
                                .Take(offset)
                                .ToList();
                }
                else if (offset < 0)
                {
                    // В будущее: сообщения новее fromMessageId
                    var fromMessage = _messages.FindOne(x => x.ChatId == chatId && x.MessageId == fromMessageId);
                    if (fromMessage == null) return new List<MessageModel>();
                    var timestamp = fromMessage.SentAt;
                    return query.Where(x => x.SentAt > timestamp)
                                .OrderBy(x => x.SentAt)
                                .Take(-offset)
                                .ToList();
                }
                return new List<MessageModel>();
            }
        }
        public List<ChatCacheClass> GetChatList()
        {
            lock (_lock)
            {
                return _chats.FindAll().ToList();
            }
        }
        public void SaveMessage(string chatId, string chatName, MessageModel message, MessageOperation operation)
        {
            lock (_lock)
            {
                var existing = _messages.FindOne(x => x.ChatId == chatId && x.MessageId == message.MessageId);

                switch (operation)
                {
                    case MessageOperation.Added:
                    case MessageOperation.Edited:
                        if (existing != null && operation == MessageOperation.Edited)
                        {
                            message.SentAt = existing.SentAt; // Сохраняем время отправки
                        }
                        _messages.Upsert(message);
                        break;
                    case MessageOperation.Deleted:
                        if (existing != null)
                        {
                            _messages.Delete(existing.MessageId);
                        }
                        break;
                }

                // Обновление чата
                var chat = _chats.FindOne(x => x.ChatId == chatId);
                if (chat == null)
                {
                    chat = new ChatCacheClass { ChatId = chatId, ChatName = chatName };
                }
                else
                {
                    chat.ChatName = chatName;
                }
                // Аватар не указан в запросе, оставляем как есть или обновляем если нужно
                // chat.AvatarFileId = ... (добавьте логику если есть источник)

                if (operation != MessageOperation.Deleted)
                {
                    chat.LastMessage = message;
                }
                else
                {
                    // Если удалили последнее, найти новое последнее
                    chat.LastMessage = _messages.Find(x => x.ChatId == chatId)
                                                .OrderByDescending(x => x.SentAt)
                                                .FirstOrDefault();
                }
                _chats.Upsert(chat);

                // Ивенты
                ChatListUpdated?.Invoke(chatId, chat.LastMessage);

                if (chatId == WPF.App.Messenger.ChatId.Value)
                {
                    ActiveChatUpdated?.Invoke(message);
                }
            }
        }

        public void Dispose()
        {
            _db.Dispose();
            _httpClient.Dispose();
        }

        // ──────────────────────────────────────────────────────────────
        // Очистка
        // ──────────────────────────────────────────────────────────────

        /// <summary>Удаляет все закешированные сообщения.</summary>
        public void ClearMessages()
        {
            lock (_lock)
            {
                _messages.DeleteAll();
            }
        }

        /// <summary>Удаляет все закешированные чаты.</summary>
        public void ClearChats()
        {
            lock (_lock)
            {
                _chats.DeleteAll();
            }
        }

        /// <summary>Удаляет файлы кеша сообщений (физические файлы + записи в БД).</summary>
        public void ClearFileCache()
        {
            lock (_lock)
            {
                var cached = _files.FindAll().ToList();
                foreach (var f in cached)
                {
                    try { if (File.Exists(f.Path)) File.Delete(f.Path); } catch { }
                }
                _files.DeleteAll();
            }
        }

        /// <summary>Полная очистка: сообщения, чаты, файлы.</summary>
        public void ClearAll()
        {
            ClearMessages();
            ClearChats();
            ClearFileCache();
        }
    }
}
