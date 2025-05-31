using BarkFluff.Client.WPF.Debug;
using BarkFluff.Client.WPF.Pages;
using BarkFluff.Client.WPF.Pages.PinCode;
using BarkFluff.Client.WPF.Pages.SetupPages;
using BarkFluff.WebApi.Core.MessengerData;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using Login = BarkFluff.Client.WPF.Pages.SetupPages.Login;

namespace BarkFluff.Client.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        private readonly HashSet<Key> _pressedKeys = new();
        #region Инициализация
        public MainWindow()
        {
            InitializeComponent();

            MainWindowBootstrap();

            ApplicationThemeManager.Apply(this);
            MouseDown += MainWindow_MouseDown;
            Closing += MainWindow_Closing;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        private void MainWindowBootstrap()
        {
#if DEBUG
            DebugBootstap();
#endif
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
                string filePath = Path.Combine(App.GParam.AppPath, "GlobalParam.json");
                GlobalParam.Save(App.GParam, filePath, App.GParam.AppPass);
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
            MainFrame.Children.Add(new WelcomPage());
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
            MainFrame.Children.Add(new Register());
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

            App.ServerCommunication.CreateAC(App.GParam, App.GParam.MachineName, SystemInfo.GetFriendlyWindowsVersion(), AppVersion.AppName, AppVersion.Version, App.GParam.IpAddress);
        }
        public void PincodeSuccess()
        {
            if (App.GParam.RefreshToken == null!)
            {
                if (App.GParam.SocketBeacon != "http://" && App.GParam.SocketBeacon != "https://" && App.GParam.SocketBeacon != string.Empty)
                {
                    App.ServerCommunication.CreateAC(App.GParam, App.GParam.MachineName, SystemInfo.GetFriendlyWindowsVersion(), AppVersion.AppName, AppVersion.Version, App.GParam.IpAddress);
                    OpenLoginPage();
                }
                else
                {
                    OpenServerListPage();
                    //если файл существует, но сокет пустой, то открываем страницу выбора сервера
                }
            }
            else
            {
                //открывать страницу с мессенджером
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
            MainFrame.Children.Add(new Register());
        }

        public void PincodeSuccessful()
        {
            MainFrame.Children.Clear();

            if (App.GParam.SocketBeacon == string.Empty)
            {
                MainFrame.Children.Add(new ServerIP());
            }
            else
            {
                MainFrame.Children.Add(new Pages.SetupPages.Login());



                //var beaconData = App.ServerCommunication.BeaconAC.GetServerInfo(new BarkFluff.Proto.Beacon.GetServerInfoRequest());



                //App.GParam.SocketIdentity = EnsureHttpPrefix(beaconData.Users.Endpoint.Host + ":" + beaconData.Identity.Endpoint.Port);
                //App.GParam.SocketUsers = EnsureHttpPrefix(beaconData.Identity.Endpoint.Host + ":" + beaconData.Users.Endpoint.Port);

                //App.GParam.ServerName = beaconData.Name;


            }
        }



        #endregion

        private void FluentWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var resolution = this.ActualWidth + "x" + this.ActualHeight;
            ResolutionTextBlock.Text = resolution;
        }
    }
}