using LiteDB;

using System.IO;
using System.Net.Http;

namespace BarkFluff.Client.WPF.Services.App.Caching
{
    public class MessageCacheManager : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _fileCacheDir;
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<CachedMessage> _messages;
        private readonly ILiteCollection<CachedFile> _files;
        private readonly object _lock = new object();
        private readonly HttpClient _httpClient = new HttpClient();
        public event Action<string, string> FileCached;

        public MessageCacheManager(string dbPath, string fileCacheDir)
        {
            _dbPath = dbPath;
            _fileCacheDir = fileCacheDir;
            Directory.CreateDirectory(fileCacheDir);
            _db = new LiteDatabase(_dbPath);
            _messages = _db.GetCollection<CachedMessage>("messages");
            _files = _db.GetCollection<CachedFile>("files");
            _messages.EnsureIndex(x => x.ChatId);
            _files.EnsureIndex(x => x.Hash);
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

            string placeholder = "pack://application:,,,/Barkfluff.Client.WPF;component/Resources/Placeholders/userplaceholder.png";

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

        public void Dispose()
        {
            _db.Dispose();
            _httpClient.Dispose();
        }
    }
}
