using BarkFluff.DBEditor.Models;
using BarkFluff.DBEditor.ViewModels;

using Wpf.Ui.Appearance;

namespace BarkFluff.DBEditor.Views
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        public MainWindow(DbCredentials creds)
        {
            InitializeComponent();
            ApplicationThemeManager.Apply(this);
            DataContext = new MainViewModel(creds);
        }
    }
}