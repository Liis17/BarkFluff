using BarkFluff.Client.WPF.MessagerData;
using BarkFluff.Client.WPF.Pages;

using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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
        public MainWindow()
        {
            InitializeComponent();

            Bootstrap();

            MWindow = this;
            ApplicationThemeManager.Apply(this);
            MouseDown += MainWindow_MouseDown;
        }

        private void Bootstrap()
        {
            GParam = new GlobalParam();
        }

        private void MainWindow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try { if (e.ChangedButton == MouseButton.Left) { this.DragMove(); } } catch { }
        }

        private void MainFrame_Loaded(object sender, RoutedEventArgs e)
        {
            MainFrame.Content = new Login();
        }

        public void RegisterStep(string host, string port)
        {
            MainFrame.Content = new Register();
        }
    }
}