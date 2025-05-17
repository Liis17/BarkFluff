using BarkFluff.Proto.Users;
using Grpc.Net.Client;

using System.Configuration;
using System.Data;
using System.Windows;
using BarkFluff.WebApi.Core;

using static BarkFluff.Proto.Users.UsersApi;
using BarkFluff.Client.WPF.Pages;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using BarkFluff.Client.WPF.ServiceWindows;

namespace BarkFluff.Client.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static BarkFluff.WebApi.Core.WebApi ServerCommunication { get; set; } = null!;
        public static BarkFluff.WebApi.Core.MessengerData.GlobalParam GParam { get; set; } = null!;

        public static MainWindow MessengerWindow { get; set; } = null!;
        public App()
        {
            
            
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string[] args = e.Args;

            foreach (string arg in args)
            {
                Console.WriteLine($"Аргумент: {arg}");
            }

            ProcessArguments(args);

            //дополнительная обработка тут если нужно (хз нужно ли)

            Bootstrap();
        }

        private void Bootstrap()
        {
            string filePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "GlobalParam.json");

            if (Debugger.IsAttached)
            {
                // Увеличивает версию
                IncrementVersion();
            }

            MessengerWindow = new MainWindow();

            if (!File.Exists(filePath))
            {
                MessengerWindow.Show();
                MessengerWindow.FirstStart();
            }
            else
            {
                
            }
        }



        private void ProcessArguments(string[] args)
        {
            
            foreach (string arg in args)
            {
                //обработка аргументов
            }
        } //обработка аргументов
        private void IncrementVersion()
        {
            var versionParts = AppVersion.Version.Split('.');
            int buildNumber = int.Parse(versionParts[3]);
            buildNumber++;
            versionParts[3] = buildNumber.ToString();
            AppVersion.Version = string.Join(".", versionParts);

            var versionFile = "K:\\source\\HavenProjects\\BarkFluff\\BarkFluff.Client.WPF\\AppVersion.cs";
            var lines = System.IO.File.ReadAllLines(versionFile);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("public static string Version"))
                {
                    lines[i] = $"       public static string Version {{ get; set; }} = \"{AppVersion.Version}\";";
                    break;
                }
            }
            System.IO.File.WriteAllLines(versionFile, lines);
        }
    }

}
