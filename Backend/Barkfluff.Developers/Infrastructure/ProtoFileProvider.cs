namespace Barkfluff.Developers.Infrastructure;

internal sealed class ProtoFileProvider : IProtoFileSource
{
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly string _protoDirectory;

    public ProtoFileProvider() : this(Path.Combine(AppContext.BaseDirectory, "Proto"))
    {
    }

    internal ProtoFileProvider(string protoDirectory)
    {
        _protoDirectory = protoDirectory;

        if (Directory.Exists(_protoDirectory))
        {
            foreach (var fileName in PublishedProtoManifest.FileNames)
            {
                var path = Path.Combine(_protoDirectory, fileName);
                if (File.Exists(path))
                    _cache[fileName] = File.ReadAllText(path);
            }
        }
    }

    public string? GetContent(string fileName)
    {
        return _cache.TryGetValue(fileName, out var content) ? content : null;
    }
}
