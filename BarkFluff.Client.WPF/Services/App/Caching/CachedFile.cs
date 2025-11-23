using LiteDB;

namespace BarkFluff.Client.WPF.Services.App.Caching
{
    public class CachedFile
    {
        public string Hash { get; set; }
        public string Path { get; set; }
    }
}
