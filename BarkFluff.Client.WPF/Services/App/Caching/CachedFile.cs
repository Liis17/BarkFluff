using LiteDB;

namespace BarkFluff.Client.WPF.Services.App.Caching
{
    public class CachedFile
    {
        [BsonId]
        public string Hash { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public FileType FileType { get; set; } = FileType.Avatar;
    }
}
