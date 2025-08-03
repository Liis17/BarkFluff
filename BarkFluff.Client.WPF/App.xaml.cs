using BarkFluff.Client.WPF.Pages;
using BarkFluff.Client.WPF.Services;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.Services.Notification;
using BarkFluff.Client.WPF.Services.Notification.System;

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BarkFluff.Client.WPF
{
    public partial class App : Application
    {
#if (DEBUG)
        private const string appUserModelId = "com.barkfluff.messenger.debug";
        private const string mutexName = "BarkFluffDebug";
#else
            private const string appUserModelId = "com.barkfluff.messenger";
            private const string mutexName = "BarkFluffMutex";
#endif





        public static string AppUserModelId { get; set; }
        public static string MutexName { get; set; }
        public static BarkFluff.WebApi.Core.WebApi ServerCommunication { get; set; } = null!;
        public static BarkFluff.WebApi.Core.MessengerData.GlobalParam GParam { get; set; } = null!;
        public static ImageColorAnalyzer ColorAnalyzer { get; set; } = null!;
        private static Mutex mutex = null!;
        private CancellationTokenSource cts = new CancellationTokenSource();
        public static MainWindow MessengerWindow { get; set; } = null!;
        public static MessageCacheManager CacheManager { get; set; } = null!;
        public App()
        {


        }
        protected override void OnStartup(StartupEventArgs e)
        {
            #region
            mutex = new Mutex(true, MutexName, out bool isNew);
            if (!isNew)
            {
                if (e.Args.Length > 0)
                {
                    // Отправляем аргумент главному экземпляру
                    BFSingleInstance.SendToExistingInstance(e.Args[0]);
                }

                Shutdown();
                return;
            }

            // Первый экземпляр — запускаем слушателя
            _ = BFSingleInstance.ListenAsync(OnBFUriReceived, cts.Token);


            if (e.Args.Length > 0)
                OnBFUriReceived(e.Args[0]); // если пришёл bf:// при запуске
            #endregion

            base.OnStartup(e);

            if (!ProtocolHelper.IsBFProtocolRegistered())
            {
                var result = MessageBox.Show("Протокол bf:// не зарегистрирован. Зарегистрировать?", "Регистрация протокола", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ProtocolRegistrar.RegisterBFProtocol();
                    MessageBox.Show("Готово. Перезапустите приложение через bf:// ссылку.");
                    Shutdown();
                    return;
                }
            }

            string[] args = e.Args;
            ProcessArguments(args);

            Bootstrap();
        }

        /// <summary>
        /// Главный метод инициализации приложения.
        /// </summary>
        private void Bootstrap()
        {
            CacheManager = new MessageCacheManager("cache.db", "Cache/");

            try
            {
                string targetPath;
#if WINDOWS_UWP // Для UWP (включая WinUI)
            folderPath = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
#else
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                targetPath = Path.Combine(Path.GetDirectoryName(exePath), "BarkFluff.Client.WPF.exe");
#endif
                string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "BarkFluff.lnk");
                ShortcutHelper.CreateShortcut(shortcutPath, targetPath, AppUserModelId);
            }
            catch
            {

            }
            AppIdHelper.SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            ServerCommunication = new WebApi.Core.WebApi();
            ColorAnalyzer = new ImageColorAnalyzer();
            string filePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "GlobalParam.json");

#if (DEBUG)
            if (Debugger.IsAttached)
            {
                // Увеличивает версию если в дебаге под отладкой
                IncrementVersion();
            }
#endif
            MessengerWindow = new MainWindow();
            MessengerWindow.Show();

            if (!File.Exists(filePath))
            {
                GParam = new BarkFluff.WebApi.Core.MessengerData.GlobalParam();
                GParam.AppPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                var machineName = Environment.MachineName;
                GParam.MachineName = machineName;
                MessengerWindow.FirstStart();
            }
            else
            {
                MessengerWindow.OpenPinCodeSecurePage();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            cts.Cancel();
            base.OnExit(e);
        }

        private void OnBFUriReceived(string uri)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show("Получен bf:// URI: " + uri);
            });
        }

        private void ProcessArguments(string[] args)
        {
            foreach (string arg in args)
            {

            }

        } //обработка аргументов
        private void IncrementVersion()
        {
            var versionParts = AppVersion.Version.Split('.');
            int buildNumber = int.Parse(versionParts[3]);
            buildNumber++;
            versionParts[3] = buildNumber.ToString();
            AppVersion.Version = string.Join(".", versionParts);

            var versionFile = "K:\\source\\HavenProjects\\BarkFluff\\BarkFluff.Client.WPF\\Services\\App\\AppVersion.cs";
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

        public static void UpdateApiClient()
        {
            ServerCommunication = null!;
            ServerCommunication = new WebApi.Core.WebApi();

            ServerCommunication.CreateAC(GParam, GParam.MachineName, SystemInfo.GetFriendlyWindowsVersion(), AppVersion.AppName, AppVersion.Version, GParam.IpAddress);
        }

        public static void OpenMessengerPage()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessengerWindow.MainFrame.Children.Clear();
                MessengerWindow.MainFrame.Children.Add(new MessengerPage());
            });

        }
    }
}
