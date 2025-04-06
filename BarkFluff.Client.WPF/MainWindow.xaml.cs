using BarkFluff.Client.WPF.MessagerData;
using BarkFluff.Client.WPF.Pages;

using Grpc.Core;

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
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        private void Bootstrap()
        {
            MWindow = this;
            string filePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "GlobalParam.json");

            if (Debugger.IsAttached)
            {
                // Увеличиваем версию
                IncrementVersion();
            }

            if (!File.Exists(filePath))
            {
                MainFrame.Children.Clear();
                MainFrame.Children.Add(new PincodeCreate());
            }
            else
            {
                MainFrame.Children.Clear();
                MainFrame.Children.Add(new PincodeSecure());
            }
        }

        private void IncrementVersion()
        {
            var versionParts = AppVersion.Version.Split('.');
            int buildNumber = int.Parse(versionParts[3]);
            buildNumber++;
            versionParts[3] = buildNumber.ToString();
            AppVersion.Version = string.Join(".", versionParts);

            // Сохраняем новую версию в файл
            SaveVersionToFile();
        }

        private void SaveVersionToFile()
        {
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
                string filePath = Path.Combine(GParam.AppPath, "GlobalParam.json");
                GlobalParam.Save(GParam, filePath, GParam.AppPass);
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


        
        #region Вход в приложение
        public void OpenPincodeSecure()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new PincodeSecure());
        }

        public void RegisterStep(string host, string port)
        {
            GParam.Host = host;
            GParam.Port = port;

            MainFrame.Children.Clear();
            MainFrame.Children.Add(new Login());
        }

        public void OpenNewProfilePage()
        {
            MainFrame.Children.Clear();
            MainFrame.Children.Add(new Register());
        }

        public void PincodeSuccessful()
        {
            if (GParam.Socket == "")
            {
                MainFrame.Children.Clear();
                MainFrame.Children.Add(new ServerIP());
            }
            else
            {
                MainFrame.Children.Clear();
                MainFrame.Children.Add(new Login());
            }
            
        }


        #endregion

        
    }
}