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
            try
            {
                MessageBox.Show("MainWindow constructor started", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                InitializeComponent();
                MessageBox.Show("InitializeComponent done", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                ApplicationThemeManager.Apply(this);
                MessageBox.Show("ApplicationThemeManager done", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                DataContext = new MainViewModel(account, credentialsService, this);
                MessageBox.Show("MainViewModel created", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);

                // Shutdown app when this window closes
                Closed += (s, e) => Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in MainWindow constructor: {ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }
    }
}
