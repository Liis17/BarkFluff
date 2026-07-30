using BarkFluff.Client.WinUI.Infrastructure.Dialogs;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Client.WinUI.Views;

using CommunityToolkit.Mvvm.Input;

using H.NotifyIcon;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

using System.ComponentModel;

namespace BarkFluff.Client.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IDialogService _dialogs;
    private bool _isExitRequested;

    public MainWindow(MainWindowViewModel viewModel, IDialogService dialogs)
    {
        _viewModel = viewModel;
        _dialogs = dialogs;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // WinUI-версия H.NotifyIcon не даёт событий клика, только команды.
        TrayIcon.DoubleClickCommand = new RelayCommand(ShowFromTray);

        AppWindow.Closing += OnAppWindowClosing;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        NavigateToCurrentViewModel();
    }

    /// <summary>Корень контента: к нему привязываются тема и <c>ContentDialog</c>.</summary>
    public FrameworkElement RootElement => Root;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MainWindowViewModel.CurrentViewModel))
        {
            NavigateToCurrentViewModel();
        }
    }

    private void NavigateToCurrentViewModel()
    {
        var viewModel = _viewModel.CurrentViewModel;
        var pageType = ViewLocator.Resolve(viewModel);
        if (pageType is null || ContentFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        ContentFrame.Navigate(pageType, viewModel, new EntranceNavigationTransitionInfo());
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs eventArgs)
    {
        // В режиме MinimizeToTray закрытие отменяется, окно прячется, процесс живёт
        // ради иконки в трее. Пункт «Выход» выставляет флаг и закрывает окно по-настоящему.
        if (!_isExitRequested && _viewModel.Settings.ClosingBehavior == WindowClosingBehavior.MinimizeToTray)
        {
            eventArgs.Cancel = true;
            this.Hide();
        }
    }

    private async void OnSettingsRequested(object sender, RoutedEventArgs eventArgs)
    {
        var view = new SettingsView { DataContext = _viewModel.Settings };
        await _dialogs.ShowContentAsync("Settings_Title", view, "Settings_Close");
    }

    private void OnTrayShowRequested(object sender, RoutedEventArgs eventArgs) => ShowFromTray();

    private void ShowFromTray()
    {
        this.Show();
        Activate();
    }

    private void OnTrayExitRequested(object sender, RoutedEventArgs eventArgs)
    {
        _isExitRequested = true;
        TrayIcon.Dispose();
        Close();
    }
}
