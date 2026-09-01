namespace Barkfluff.Developers.Infrastructure;

internal sealed class PublishedProtoCatalog : IPublishedProtoCatalog
{
    private readonly IProtoFileSource _source;

    public PublishedProtoCatalog(IProtoFileSource source)
    {
        _source = source;
    }

    public IReadOnlyList<string> PublishedFileNames => PublishedProtoManifest.FileNames;

    public bool IsPublished(string fileName)
    {
        return PublishedProtoManifest.Contains(fileName);
    }

    public string? GetContent(string fileName)
    {
        return IsPublished(fileName) ? _source.GetContent(fileName) : null;
    }

    public IReadOnlyList<string> GetMissingFiles()
    {
        return PublishedFileNames
            .Where(fileName => GetContent(fileName) is null)
            .ToArray();
    }
}
