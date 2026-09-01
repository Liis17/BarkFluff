namespace Barkfluff.Developers.Infrastructure;

public interface IPublishedProtoCatalog
{
    IReadOnlyList<string> PublishedFileNames { get; }

    bool IsPublished(string fileName);

    string? GetContent(string fileName);

    IReadOnlyList<string> GetMissingFiles();
}
