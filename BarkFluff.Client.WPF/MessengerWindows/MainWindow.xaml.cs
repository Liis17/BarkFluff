using BarkFluff.Client.WPF.Debugs;
using BarkFluff.Client.WPF.Pages;
using BarkFluff.Client.WPF.Pages.PinCode;
using BarkFluff.Client.WPF.Pages.SetupPages;
using BarkFluff.WebApi.Core.MessengerData;

using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using Login = BarkFluff.Client.WPF.Pages.SetupPages.Login;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF
{
    public partial class MainWindow : FluentWindow
    {
        private readonly HashSet<Key> _pressedKeys = new();
        #region Инициализация
        public MainWindow()
        {
            InitializeComponent();

            MainWindowBootstrap();

            MouseDown += MainWindow_MouseDown;
            Closing += MainWindow_Closing;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            //ExecuteComboAction();
        }
        private void MainWindowBootstrap()
        {
#if DEBUG
            DebugBootstap();
#endif

            //применение темы
            ThemeLoader();
        }
        private void ThemeLoader()
        {
            ApplicationThemeManager.ApplySystemTheme();
            var systemAccentColor = SystemParameters.WindowGlassColor;
            var accentBrush = new SolidColorBrush(systemAccentColor);
            accentBrush.Freeze(); // Для производительности

            // Перезаписываем ресурсы библиотеки Wpf.Ui на системный цвет
            Application.Current.Resources["SystemAccentColorPrimaryBrush"] = accentBrush;
            Application.Current.Resources["SystemAccentBrush"] = accentBrush; // Для совместимости
            Application.Current.Resources["AccentTextFillColorPrimaryBrush"] = accentBrush; // Для текста
        }

#if DEBUG
        private void DebugBootstap()
        {
            this.KeyDown += OnKeyDown;
            this.KeyUp += OnKeyUp;
        }
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            _pressedKeys.Add(e.Key);

            if (_pressedKeys.Contains(Key.Space) && _pressedKeys.Contains(Key.Escape))
            {
                ExecuteComboAction();
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            //_pressedKeys.Remove(e.Key);
        }

        private void ExecuteComboAction()
        {
            var _debugWindow = new DebugWindow();
            _debugWindow.Show();
        }
#endif





        private void MainFrame_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void Loaded_VersionText(object sender, RoutedEventArgs e)
        {
            VersionTextBlock.Text = AppVersion.VersionName + " " + AppVersion.Version;
        }
        #endregion

        #region Window Events
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                SaveSettings();
            }
            catch
            {
                // ignored
            }
        }
        public static void SaveSettings()
        {
            try
            {
                string filePath = Path.Combine(SystemInfo.GetAppPath(), "datas", "GlobalParam.json");
                if (App.GParam == null) { return; }
                if (string.IsNullOrEmpty(App.GParam.AppPass) || string.IsNullOrEmpty(App.GParam.AppPath)) { return; }
                GlobalParam.Save(App.GParam, filePath, App.GParam.AppPass);
                App.ErideMessage.AddMessage($"Сохранение настроек: {filePath}", new Erida { Type = MType.Debug });
            }
            catch
            {
                // ignored
            }
        }
        private void MainWindow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            //try { if (e.ChangedButton == MouseButton.Left) { this.DragMove(); } } catch { }
        }
        #endregion

        #region Первый запуск приложения
        public void FirstStart()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new WelcomePage());
        }
        public void OpenCreatePinCodePage()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new CreatePinCodePage());
        }
        public void OpenPinCodeSecurePage()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new PincodeSecure());
        }
        public void OpenCreateAccountPage()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new CreateAccount());
        }
        public void OpenServerListPage()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new SelectServer());
        }
        public void OpenLoginPage()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new Login());

            var response = App.ServerCommunication.CreateAC(App.GParam, App.GParam.MachineName, SystemInfo.GetFriendlyWindowsVersion(), AppVersion.AppName, AppVersion.Version, App.GParam.IpAddress);
            if (!response.IsSuccess)
            {
                App.ErideMessage.AddMessage(response.ErrorMessage ?? "Неизвестная проблема", new Erida { Type = MType.Error });
                return;
            }
            else
            {
                App.ErideMessage.AddMessage("API клиент успешно обновлён", new Erida { Type = MType.Debug });
            }

        }
        public void OpenPasswordRecoveryPage()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new PasswordReset());
        }
        public void PincodeSuccess()
        {
            if (App.GParam.RefreshToken == null!)
            {
                if (App.GParam.SocketBeacon != "http://" && App.GParam.SocketBeacon != "https://" && App.GParam.SocketBeacon != string.Empty)
                {
                    var response = App.ServerCommunication.CreateAC(App.GParam, App.GParam.MachineName, SystemInfo.GetFriendlyWindowsVersion(), AppVersion.AppName, AppVersion.Version, App.GParam.IpAddress);
                    if (!response.IsSuccess)
                    {
                        App.ErideMessage.AddMessage(response.ErrorMessage ?? "Неизвестная проблема", new Erida { Type = MType.Error });
                        return;
                    }
                    else
                    {
                        App.ErideMessage.AddMessage("API клиент успешно обновлён", new Erida { Type = MType.Debug });
                    }
                    OpenLoginPage();
                }
                else
                {
                    OpenServerListPage();
                    //если файл существует, но сокет пустой, то открываем страницу выбора сервера
                }
            }
            else if (!string.IsNullOrEmpty(App.GParam.SocketBeacon) &&
                !string.IsNullOrEmpty(App.GParam.SocketFiles) &&
                !string.IsNullOrEmpty(App.GParam.SocketIdentity) &&
                !string.IsNullOrEmpty(App.GParam.SocketUsers) &&
                !string.IsNullOrEmpty(App.GParam.RefreshToken.Value) &&
                !string.IsNullOrEmpty(App.GParam.ServerName)
                )
            {
                App.OpenMessengerPage();
            }

        }

        #endregion

        #region Вход в приложение


        public void OpenPincodeSecure()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new PincodeSecure());
        }

        public void RegisterStep(string socket)
        {
            App.GParam.SocketBeacon = socket;

            PincodeSuccessful();
        }

        public void OpenNewProfilePage()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new CreateAccount());
        }

        public void PincodeSuccessful()
        {
            MainFrame.Children.Clear();

            if (App.GParam.SocketBeacon == string.Empty)
            {
                MainFrame.Children.Add(new SelectServer());
            }
            else
            {
                MainFrame.Children.Add(new Pages.SetupPages.Login());
            }
        }



        #endregion

        private void FluentWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var resolution = this.ActualWidth + "x" + this.ActualHeight;
            ResolutionTextBlock.Text = resolution;
        }

        private void ContentPanel_Loaded(object sender, RoutedEventArgs e)
        {
            App.CreateEridaMessage(ContentPanel);
        }
    }
}