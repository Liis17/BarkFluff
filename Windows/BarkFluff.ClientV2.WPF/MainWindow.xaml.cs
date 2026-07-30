using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.ViewModels;

using System.ComponentModel;
using System.Windows;

using Wpf.Ui.Controls;

namespace BarkFluff.ClientV2.WPF;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private bool _isExitRequested;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Скрытое окно остаётся в Application.Windows, поэтому приложение продолжает работать.
        // Выход из трея выставляет флаг и закрывает окно уже по-настоящему.
        if (!_isExitRequested && _viewModel.Settings.ClosingBehavior == WindowClosingBehavior.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void OnTrayShowRequested(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnTrayExitRequested(object sender, RoutedEventArgs e)
    {
        _isExitRequested = true;
        Close();
    }
}
