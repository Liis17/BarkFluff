using BarkFluff.Client.WPF.Debug;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.Client.WPF.Pages;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using BarkFluff.Client.WPF.Pages.SetupPages;

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
            try { if (e.ChangedButton == MouseButton.Left) { this.DragMove(); } } catch { }
        }
        #endregion

        #region Первый запуск приложения
        public void FirstStart()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new WelcomPage());
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
                MainFrame.Children.Add(new Login());

                

                //var beaconData = App.ServerCommunication.BeaconAC.GetServerInfo(new BarkFluff.Proto.Beacon.GetServerInfoRequest());



                //App.GParam.SocketIdentity = EnsureHttpPrefix(beaconData.Users.Endpoint.Host + ":" + beaconData.Identity.Endpoint.Port);
                //App.GParam.SocketUsers = EnsureHttpPrefix(beaconData.Identity.Endpoint.Host + ":" + beaconData.Users.Endpoint.Port);

                //App.GParam.ServerName = beaconData.Name;

                
            }
        }


        #endregion


    }
}