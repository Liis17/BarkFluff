using System.IO;

using Windows.ApplicationModel;
using Windows.Storage;

namespace BarkFluff.Client.Core.Infrastructure.Storage;

public sealed class AppDataPaths
{
    public AppDataPaths(string applicationDirectory)
    {
        DataDirectory = Path.Combine(applicationDirectory, "data");
        DatabasePath = Path.Combine(DataDirectory, "barkfluff.db");
    }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    /// <summary>
    /// Каталог установки MSIX-пакета доступен только на чтение, поэтому изменяемые данные
    /// живут в <c>LocalState</c>. Запуск без пакетной идентичности (тесты, F5 без деплоя)
    /// откатывается на <c>%LOCALAPPDATA%\BarkFluff</c>.
    /// </summary>
    public static AppDataPaths CreateDefault()
    {
        string root;
        try
        {
            _ = Package.Current;
            root = ApplicationData.Current.LocalFolder.Path;
        }
        catch (InvalidOperationException)
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BarkFluff");
        }

        return new AppDataPaths(root);
    }
}
