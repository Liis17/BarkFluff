using System.IO;

namespace BarkFluff.Client.Core.Infrastructure.Storage;

/// <summary>
/// Разовый перенос базы из раскладки «рядом с exe», которую использовал WPF-клиент, в
/// <c>LocalState</c> MSIX-пакета. DPAPI-блоба это не касается: они привязаны к пользователю,
/// а не к пакету, поэтому сохранённая сессия и ключи приватных чатов остаются валидными.
/// </summary>
public static class LegacyDatabaseImporter
{
    private static readonly string[] SidecarExtensions = ["-wal", "-shm"];

    public static void TryImport(AppDataPaths target)
    {
        if (File.Exists(target.DatabasePath))
        {
            return;
        }

        var source = EnumerateLegacyDatabasePaths()
            .FirstOrDefault(path =>
                File.Exists(path)
                && !string.Equals(path, target.DatabasePath, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(target.DataDirectory);
            File.Copy(source, target.DatabasePath);

            foreach (var extension in SidecarExtensions)
            {
                if (File.Exists(source + extension))
                {
                    File.Copy(source + extension, target.DatabasePath + extension, overwrite: true);
                }
            }
        }
        catch (IOException)
        {
            // Импорт опортунистический: при неудаче клиент просто стартует с чистой базой.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static IEnumerable<string> EnumerateLegacyDatabasePaths()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BarkFluff",
            "data",
            "barkfluff.db");

        yield return Path.Combine(AppContext.BaseDirectory, "data", "barkfluff.db");
    }
}
