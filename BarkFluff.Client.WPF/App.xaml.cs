using BarkFluff.Client.WPF.Pages;
using BarkFluff.Client.WPF.Services;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.Services.App.Update;
using BarkFluff.Client.WPF.Services.Erida;
using BarkFluff.Client.WPF.Services.Notification;
using BarkFluff.Client.WPF.Services.Notification.System;
using BarkFluff.WebApi.Core.MessengerData;

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BarkFluff.Client.WPF
{
    public partial class App : Application
    {
#if (DEBUG)
        private const string AppUserModelId = "com.barkfluff.messenger.debug";
        private const string MutexName = "BarkFluffDebug";
#else
        private const string AppUserModelId = "com.barkfluff.messenger";
        private const string MutexName = "BarkFluffMutex";
#endif

        public static BarkFluff.WebApi.Core.WebApi ServerCommunication { get; set; } = null!;
        public static BarkFluff.WebApi.Core.MessengerData.GlobalParam GParam { get; set; } = null!;
        public static ImageColorAnalyzer ColorAnalyzer { get; set; } = null!;
        private static Mutex mutex = null!;
        private CancellationTokenSource cts = new CancellationTokenSource();
        public static MainWindow MessengerWindow { get; set; } = null!;
        public static MessengerPage Messenger { get; set; } = null!;
        public static MessageCacheManager CacheManager { get; set; } = null!;
        public static FileCacheService FileCacheService { get; set; } = null!;
        public static DropMessage ErideMessage { get; set; } = null!;

        private static UpdateService updateService { get; set; } = null!;
        public App() { }
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
            Directory.CreateDirectory("temp");
            Directory.CreateDirectory("datas");
            Directory.CreateDirectory("datas\\cache");
            CacheManager = new MessageCacheManager("datas\\cache.db", "temp");
            FileCacheService = new FileCacheService("datas\\file_cache.db", "datas\\cache");

            string targetPath = string.Empty;
            try
            {

#if WINDOWS_UWP
                folderPath = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
#else
                var baseDir = AppContext.BaseDirectory;
                targetPath = Path.Combine(baseDir, "BarkFluff.Client.WPF.exe");
#endif
                string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "BarkFluff.lnk");
                if (!File.Exists(shortcutPath))
                {
                    ShortcutHelper.CreateShortcut(shortcutPath, targetPath, AppUserModelId);
                }
            }
            catch { }
            AppIdHelper.SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            ServerCommunication = new WebApi.Core.WebApi();
            ColorAnalyzer = new ImageColorAnalyzer();
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(targetPath) ?? string.Empty, "datas"));
            string filePath = Path.Combine(Path.GetDirectoryName(targetPath), "datas", "GlobalParam.json");

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
                GParam.AppPath = Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty;
                var machineName = Environment.MachineName;
                GParam.MachineName = machineName;
                MessengerWindow.FirstStart();
            }
            else
            {
                MessengerWindow.OpenPinCodeSecurePage();
            }
        }
        public static void CreateEridaMessage(StackPanel stack)
        {
            ErideMessage = new DropMessage(stack);

            updateService = new UpdateService(AppVersion.Version, AppVersion.VersionType);
            updateService.Start();
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

            var versionFile = "K:\\source\\BarkFluff\\BarkFluff.Client.WPF\\Services\\App\\AppVersion.cs";
            var lines = System.IO.File.ReadAllLines(versionFile);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("public static string Version"))
                {
                    lines[i] = $"        public static string Version {{ get; set; }} = \"{AppVersion.Version}\";";
                    break;
                }
            }
            System.IO.File.WriteAllLines(versionFile, lines);
        }

        /// <summary>
        /// Метод для обновления API клиента.
        /// </summary>
        public static async void UpdateApiClient()
        {
            ServerCommunication = null!;
            ServerCommunication = new WebApi.Core.WebApi();
            var response = ServerCommunication.CreateAC(GParam, GParam.MachineName, SystemInfo.GetFriendlyWindowsVersion(), AppVersion.AppName, AppVersion.Version, GParam.IpAddress);
            if (!response.IsSuccess)
            {
                App.ErideMessage.AddMessage(response.ErrorMessage ?? "Не удалось обновить API клиент", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                return;
            }
            else
            {
                App.ErideMessage.AddMessage("API клиент успешно обновлён", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Debug });
            }
            var (error, serverInfo) = await ServerCommunication.GetServerInfo(GParam);
            if (!error.IsSuccess)
            {
                App.ErideMessage.AddMessage(error.ErrorMessage ?? "Не удалось получить информацию о сервере", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                return;
            }
            else
            {
                App.GParam.ServerName = serverInfo.Name;
                App.GParam.ServerDescription = serverInfo.Description;
                App.GParam.SocketIdentity = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Identity.Endpoint.Host + ":" + serverInfo.Identity.Endpoint.Port);
                App.GParam.SocketUsers = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Users.Endpoint.Host + ":" + serverInfo.Users.Endpoint.Port);
                App.GParam.SocketFiles = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Files.Endpoint.Host + ":" + serverInfo.Files.Endpoint.Port);
                App.GParam.SocketMessages = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Messages.Endpoint.Host + ":" + serverInfo.Messages.Endpoint.Port);
                App.GParam.SocketUpdates = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Updates.Endpoint.Host + ":" + serverInfo.Updates.Endpoint.Port);
                App.GParam.Colors = new ClientColors()
                {
                    LiteHex = serverInfo.Color.LiteHex,
                    MainHex = serverInfo.Color.MainHex,
                    HardHex = serverInfo.Color.HardHex,
                };
                string filePath = Path.Combine(AppContext.BaseDirectory, "datas", "GlobalParam.json");
                if (App.GParam == null) { return; }
                if (string.IsNullOrEmpty(App.GParam.AppPass) || string.IsNullOrEmpty(App.GParam.AppPath)) { return; }
                GlobalParam.Save(App.GParam, filePath, App.GParam.AppPass);
            }


        }

        public static void OpenMessengerPage()
        {

            Application.Current.Dispatcher.Invoke(() =>
            {
                Messenger = new MessengerPage();
                MessengerWindow.MainFrame.Children.Clear();
                MessengerWindow.MainFrame.Children.Add(Messenger);
            });

        }
    }
}
