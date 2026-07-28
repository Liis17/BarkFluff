using System.IO;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Storage;

public sealed class AppDataPaths
{
    public AppDataPaths(string applicationDirectory)
    {
        DataDirectory = Path.Combine(applicationDirectory, "data");
        DatabasePath = Path.Combine(DataDirectory, "barkfluff.db");
    }

    public string DataDirectory { get; }

    public string DatabasePath { get; }
}
