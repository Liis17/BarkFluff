using BarkFluff.DBEditor.Models;
using BarkFluff.DBEditor.Services;
using BarkFluff.DBEditor.ViewModels;

using System.Windows;

using Wpf.Ui.Appearance;

namespace BarkFluff.DBEditor.Views
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        public MainWindow(SavedAccount account, CredentialsService credentialsService)
        {
            InitializeComponent();
            ApplicationThemeManager.Apply(this);
            DataContext = new MainViewModel(account, credentialsService, this);

            // Shutdown app when this window closes
            Closed += (s, e) => Application.Current.Shutdown();
        }
    }
}
