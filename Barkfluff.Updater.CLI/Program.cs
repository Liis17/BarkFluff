using System;
using System.Threading;

namespace Barkfluff.Updater.CLI
{
    public class Program
    {
        static void Main(string[] args)
        {
            string mode = "default";
            if (args.Length > 0)
            {
                if (args[0] == "-update")
                    mode = "update";
                else if (args[0] == "-install")
                    mode = "install";
            }

            string[] logoLines = new string[]
            {
                "┌─────────────────────────────────────────────────────────────────────┐",
                "│ ██████   ██   ██████  ██  ██  ██████ ██      ██   ██ ██████ ██████  │",
                "│ ██  ██  ████  ██   ██ ██ ██   ██     ██      ██   ██ ██     ██      │",
                "│ █████  ██  ██ ██████  ████    █████  ██      ██   ██ █████  █████   │",
                "│ █████  ██████ ██ ██   ██ ██   ██     ██      ██   ██ ██     ██      │",
                "│ ██  ██ ██  ██ ██  ██  ██  ██  ██     ██      ██   ██ ██     ██      │",
                "│ ██████ ██  ██ ██   ██ ██   ██ ██     █████   █████   ██     ██      │",
                "└─────────────────────────────────────────────────────────────────────┘"
            };

            string[] updateLines = new string[]
            {
                "┌─────────────────────────────────────────────────────────────────────┐",
                "│ ██████   ██   ██████  ██  ██  ██████ ██      ██   ██ ██████ ██████  │",
                "│ ██  ██  ████  ██   ██ ██ ██   ██     ██      ██   ██ ██     ██      │",
                "│ █████  ██  ██ ██████  ████    █████  ██      ██   ██ █████  █████   │",
                "│ █████  ██████ ██ ██   ██ ██   ██     ██      ██   ██ ██     ██      │",
                "│ ██  ██ ██  ██ ██  ██  ██  ██  ██     ██      ██   ██ ██     ██      │",
                "│ ██████ ██  ██ ██   ██ ██   ██ ██     █████   █████   ██     ██      │",
                "└─────────────────────────┐                                        ┌──┘",
                "                          │ █  █ ████  ███   ██  █████ ████ ████   │",
                "                          │ █  █ █  █  █  █ █  █   █   █    █  █   │",
                "                          │ █  █ ████  █  █ ████   █   ███  ████   │",
                "                          │ █  █ █     █  █ █  █   █   █    █ █    │",
                "                          │ ████ █     ███  █  █   █   ████ █  █   │",
                "                          └────────────────────────────────────────┘"
            };

            string[] installLines = new string[]
            {
                "┌─────────────────────────────────────────────────────────────────────┐",
                "│ ██████   ██   ██████  ██  ██  ██████ ██      ██   ██ ██████ ██████  │",
                "│ ██  ██  ████  ██   ██ ██ ██   ██     ██      ██   ██ ██     ██      │",
                "│ █████  ██  ██ ██████  ████    █████  ██      ██   ██ █████  █████   │",
                "│ █████  ██████ ██ ██   ██ ██   ██     ██      ██   ██ ██     ██      │",
                "│ ██  ██ ██  ██ ██  ██  ██  ██  ██     ██      ██   ██ ██     ██      │",
                "│ ██████ ██  ██ ██   ██ ██   ██ ██     █████   █████   ██     ██      │",
                "└─────────────────────────┐                                           └──┐",
                "                          │ ████ █  █  ███ █████  ██  █    █    ████ ████│",
                "                          │  ██  ██ █ █      █   █  █ █    █    █    █  █│",
                "                          │  ██  █ ██  ███   █   ████ █    █    ███  ████│",
                "                          │  ██  █  █    █   █   █  █ █    █    █    █ █ │",
                "                          │ ████ █  █ ███    █   █  █ ████ ████ ████ █  █│",
                "                          └──────────────────────────────────────────────┘"
            };

            string[] lines;
            switch (mode)
            {
                case "update":
                    lines = updateLines;
                    break;
                case "install":
                    lines = installLines;
                    break;
                default:
                    lines = logoLines;
                    break;
            }

            PrintWithGradient(lines);

            Console.ResetColor();
            Console.ReadLine();
        }

        static void PrintWithGradient(string[] lines)
        {
            int totalRows = lines.Length;
            int maxCols = 0;
            foreach (var line in lines)
            {
                if (line.Length > maxCols)
                    maxCols = line.Length;
            }

            // Цвета градиента: #ff4141 -> #d69d85
            int startR = 0xff, startG = 0x41, startB = 0x41;
            int endR = 0xd6, endG = 0x9d, endB = 0x85;

            for (int row = 0; row < lines.Length; row++)
            {
                string line = lines[row];
                for (int col = 0; col < line.Length; col++)
                {
                    // Диагональный градиент: позиция от 0 до 1 по диагонали
                    double t = (double)(row + col) / (totalRows + maxCols - 2);

                    int r = (int)(startR + (endR - startR) * t);
                    int g = (int)(startG + (endG - startG) * t);
                    int b = (int)(startB + (endB - startB) * t);

                    Console.Write($"\x1b[38;2;{r};{g};{b}m{line[col]}");
                }
                Console.WriteLine("\x1b[0m");
                Thread.Sleep(40);
            }
        }
    }
}
