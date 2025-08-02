using LiteDB;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BarkFluff.Client.WPF.Services.App.Caching
{
    public class MessageCacheManager : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _fileCacheDir;
        private readonly LiteDatabase _db;

        private readonly ILiteCollection<CachedMessage> _messages;
        private readonly ILiteCollection<CachedFile> _files;

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

        public List<CachedMessage> GetMessagesForChat(string chatId)
        {
            return _messages
                .Find(x => x.ChatId == chatId)
                .OrderBy(x => x.Timestamp)
                .ToList();
        }

        public void SaveMessage(CachedMessage msg)
        {
            if (_messages.Exists(x => x.Id == msg.Id))
                return;

            _messages.Insert(msg);
        }

        public bool HasFile(string fileId)
        {
            return _files.Exists(f => f.FileId == fileId);
        }

        public string? GetFilePath(string fileId)
        {
            var file = _files.FindById(fileId);
            return file?.LocalPath;
        }

        public void SaveFile(string fileId, string hash, string originalFileName, byte[] content)
        {
            if (_files.Exists(x => x.FileId == fileId))
                return;

            string ext = Path.GetExtension(originalFileName);
            string fileName = $"{hash}{ext}";
            string fullPath = Path.Combine(_fileCacheDir, fileName);

            File.WriteAllBytes(fullPath, content);

            _files.Insert(new CachedFile
            {
                FileId = fileId,
                Hash = hash,
                LocalPath = fullPath
            });
        }

        public long? GetLastMessageId(string chatId)
        {
            return _messages
                .Find(x => x.ChatId == chatId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefault()?.Id;
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}
