using BarkFluff.Client.WPF.MessagerData;
using BarkFluff.Client.WPF.Pages;

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

namespace BarkFluff.Client.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        public static MainWindow MWindow { get; private set; } = null!;
        public static GlobalParam GParam { get; set; } = null;

        #region Инициализация
        public MainWindow()
        {
            InitializeComponent();

            Bootstrap();

            ApplicationThemeManager.Apply(this);
            MouseDown += MainWindow_MouseDown;
            Closing += MainWindow_Closing;
        }
        private void Bootstrap()
        {
            MWindow = this;
            //GParam = new GlobalParam();
            string exePath = Assembly.GetExecutingAssembly().Location;
            string exeDirectory = Path.GetDirectoryName(exePath);
            string filePath = Path.Combine(exeDirectory, "GlobalParam.json");
            

            if (!File.Exists(filePath))
            {
                MainFrame.Navigate(new PincodeCreate());
                MainFrame.NavigationService.RemoveBackEntry();
            }
            else
            {
                MainFrame.Navigate(new PincodeSecure());
                MainFrame.NavigationService.RemoveBackEntry();
            }
        }

        private void MainFrame_Loaded(object sender, RoutedEventArgs e)
        {

        }
        #endregion

        #region Window Events
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            string filePath = Path.Combine(GParam.AppPath, "GlobalParam.json");
            GlobalParam.Save(GParam, filePath, GParam.AppPass);
        }
        private void MainWindow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try { if (e.ChangedButton == MouseButton.Left) { this.DragMove(); } } catch { }
        }
        #endregion


        
        #region Вход в приложение
        public void OpenPincodeSecure()
        {
            MainFrame.Navigate(new PincodeSecure());
            MainFrame.NavigationService.RemoveBackEntry();
        }

        public void RegisterStep(string host, string port)
        {
            GParam.Host = host;
            GParam.Port = port;

            MainFrame.Navigate(new Register());
            MainFrame.NavigationService.RemoveBackEntry();
        }

        public void PincodeSuccessful()
        {
            MainFrame.Navigate(new Register());
            MainFrame.NavigationService.RemoveBackEntry();
        }
        #endregion



    }
}