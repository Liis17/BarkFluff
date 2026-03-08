using Barkfluff.Updater.CLI.Arguments;
using Barkfluff.Updater.CLI.UI;

namespace Barkfluff.Updater.CLI.Commands
{
    /// <summary>
    /// Команда отображения справки
    /// </summary>
    public class HelpCommand
    {
        public int Execute(string[] invalidArguments)
        {
            // Выводим логотип
            ConsoleUI.PrintWithGradient(LogoAssets.LogoLines);

            // Если есть невалидные аргументы - показываем их
            if (invalidArguments != null && invalidArguments.Length > 0)
            {
                ConsoleUI.PrintInvalidArguments(invalidArguments);
            }

            // Показываем справку
            ConsoleUI.PrintHelp(ArgumentParser.GetAvailableArguments());

            return 0;
        }
    }
}
