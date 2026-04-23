namespace Barkfluff.Developers.Infrastructure;

public class ProtoFileProvider
{
    private readonly Dictionary<string, string> _cache = new();
    private readonly string _protoDirectory;

    public ProtoFileProvider()
    {
        _protoDirectory = Path.Combine(AppContext.BaseDirectory, "Proto");

        if (Directory.Exists(_protoDirectory))
        {
            foreach (var file in Directory.GetFiles(_protoDirectory, "*.proto"))
            {
                var name = Path.GetFileName(file);
                _cache[name] = File.ReadAllText(file);
            }
        }
    }

    public string? GetContent(string fileName)
    {
        return _cache.TryGetValue(fileName, out var content) ? content : null;
    }

    public List<string> GetAvailableFiles()
    {
        return _cache.Keys.OrderBy(k => k).ToList();
    }
}
