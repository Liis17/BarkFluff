using BarkFluff.Client.WPF.Pages;
using BarkFluff.Client.WPF.ServiceWindows;
using BarkFluff.Proto.Users;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;

using Grpc.Net.Client;

using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

using static BarkFluff.Proto.Users.UsersApi;

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
            ServerCommunication = new WebApi.Core.WebApi();
            string filePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "GlobalParam.json");
            GParam = new BarkFluff.WebApi.Core.MessengerData.GlobalParam();
            if (Debugger.IsAttached)
            {
                // Увеличивает версию
                IncrementVersion();
            }
            GParam.AppPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;

            var machineName = Environment.MachineName;
            GParam.MachineName = machineName;

            MessengerWindow = new MainWindow();

            if (!File.Exists(filePath))
            {
                MessengerWindow.Show();
                MessengerWindow.FirstStart();


                

            }
            else
            {
                if (GParam.SocketBeacon != "http://" || GParam.SocketBeacon != string.Empty)
                {
                    ServerCommunication.CreateAC(GParam, GParam.MachineName);
                }
                else
                {
                    MessengerWindow.FirstStart();
                    //если файл существует, но сокет пустой, то открываем страницу выбора сервера
                }

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
