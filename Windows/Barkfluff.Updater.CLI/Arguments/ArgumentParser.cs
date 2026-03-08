namespace Barkfluff.Updater.CLI.Arguments
{
    /// <summary>
    /// –ежим работы приложени€
    /// </summary>
    public enum AppMode
    {
        Help,
        Install,
        Update,
        AutoUpdate  // јвтоматическое обновление при обнаружении Barkfluff.exe р€дом
    }

    /// <summary>
    /// –езультат парсинга аргументов
    /// </summary>
    public class ParsedArguments
    {
        public AppMode Mode { get; set; } = AppMode.Help;
        public bool Silent { get; set; } = false;
        public string[] InvalidArguments { get; set; } = new string[0];
        public bool HasInvalidArguments => InvalidArguments.Length > 0;
    }

    /// <summary>
    /// ѕарсер аргументов командной строки
    /// </summary>
    public static class ArgumentParser
    {
        private static readonly Dictionary<string, AppMode> ModeArguments = new Dictionary<string, AppMode>(StringComparer.OrdinalIgnoreCase)
        {
            { "-install", AppMode.Install },
            { "--install", AppMode.Install },
            { "-i", AppMode.Install },
            { "-update", AppMode.Update },
            { "--update", AppMode.Update },
            { "-u", AppMode.Update },
            { "-help", AppMode.Help },
            { "--help", AppMode.Help },
            { "-h", AppMode.Help },
            { "-?", AppMode.Help },
            { "/?", AppMode.Help }
        };

        private static readonly HashSet<string> SilentArguments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-silent",
            "--silent",
            "-s",
            "-q",
            "--quiet"
        };

        public static ParsedArguments Parse(string[] args)
        {
            var result = new ParsedArguments();
            var invalidArgs = new List<string>();
            bool modeSet = false;

            foreach (var arg in args)
            {
                if (ModeArguments.TryGetValue(arg, out var mode))
                {
                    result.Mode = mode;
                    modeSet = true;
                }
                else if (SilentArguments.Contains(arg))
                {
                    result.Silent = true;
                }
                else if (arg.StartsWith("-") || arg.StartsWith("/"))
                {
                    invalidArgs.Add(arg);
                }
            }

            // ≈сли режим не задан и нет невалидных аргументов - провер€ем локальную установку
            if (!modeSet && invalidArgs.Count == 0)
            {
                result.Mode = AppMode.AutoUpdate;
            }

            result.InvalidArguments = invalidArgs.ToArray();
            return result;
        }

        public static UI.ArgumentInfo[] GetAvailableArguments()
        {
            return new UI.ArgumentInfo[]
            {
                new UI.ArgumentInfo(new[] { "--install", "-install", "-i" }, "Install BarkFluff to AppData"),
                new UI.ArgumentInfo(new[] { "--update", "-update", "-u" }, "Update existing installation"),
                new UI.ArgumentInfo(new[] { "--silent", "-silent", "-s", "-q" }, "Silent mode (no prompts)"),
                new UI.ArgumentInfo(new[] { "--help", "-help", "-h", "-?" }, "Show help")
            };
        }
    }
}
