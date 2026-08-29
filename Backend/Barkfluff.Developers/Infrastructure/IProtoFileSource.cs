namespace Barkfluff.Developers.Infrastructure;

internal interface IProtoFileSource
{
    string? GetContent(string fileName);

    IReadOnlyList<string> GetAvailableFiles();
}
