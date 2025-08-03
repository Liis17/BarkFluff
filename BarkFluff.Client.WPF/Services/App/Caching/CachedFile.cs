using LiteDB;

namespace BarkFluff.Client.WPF.Services.App.Caching
{
    public class CachedFile
    {
        [BsonId]
        public string FileId { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public string LocalPath { get; set; } = string.Empty;
    }
}
