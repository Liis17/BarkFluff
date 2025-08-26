using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;

namespace Barkfluff.Updater
{
    public class Program
    {
        static void Main(string[] args)
        {
            //настройка цветов консоли
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Clear();

            Console.WriteLine("Обновление BarkFluff...");


            string currentDir = Directory.GetCurrentDirectory();
            string updatesDir = Path.Combine(currentDir, "updates");
            string zipFile = Path.Combine(updatesDir, "wpf-build.zip");
            string hashFile = Path.Combine(updatesDir, "release.hash");
            string exeFile = Path.Combine(currentDir, "BarkFluff.Client.WPF.exe");

            if (!Directory.Exists(updatesDir))
            {
                Console.WriteLine("Не могу обновиться: папка updates не найдена.");
                return;
            }

            if (!File.Exists(zipFile))
            {
                Console.WriteLine("Не могу обновиться: файл wpf-build.zip не найден.");
                return;
            }

            if (!File.Exists(hashFile))
            {
                Console.WriteLine("Не могу обновиться: файл release.hash не найден.");
                return;
            }

            using (var sha256 = SHA256.Create())
            using (var fileStream = File.OpenRead(zipFile))
            {
                byte[] hash = sha256.ComputeHash(fileStream);
                string shaHash = BitConverter.ToString(hash).Replace("-", "").ToLower();
                string originalHash = File.ReadAllText(hashFile).Trim().ToLower().Replace("sha256:","");
                if (shaHash != originalHash)
                {
                    Console.WriteLine("Целостность файла нарушена: SHA256 не совпадает.");
                    return;
                }
            }

            if (File.Exists(exeFile))
            {
                File.Delete(exeFile);
            }

            ZipFile.ExtractToDirectory(zipFile, currentDir, true);

            Process.Start(exeFile);

            Environment.Exit(0);
        }
    }
}
