using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Client.WinUI.Views;

using CommunityToolkit.Mvvm.Input;

using H.NotifyIcon;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

using System.ComponentModel;

using Windows.Graphics;

namespace BarkFluff.Client.WinUI;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan WindowSizeSaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly MainWindowViewModel _viewModel;
    private bool _isExitRequested;
    private CancellationTokenSource? _windowSizeSaveCancellation;
    private Task? _windowSizeSaveTask;
    private bool _windowCloseReady;
    private bool _windowCloseInProgress;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        ApplySavedWindowSize();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // WinUI-версия H.NotifyIcon не даёт событий клика, только команды.
        TrayIcon.DoubleClickCommand = new RelayCommand(ShowFromTray);

        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        _viewModel.Settings.WindowSizeResetRequested += OnWindowSizeResetRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        NavigateToCurrentViewModel();
    }

    /// <summary>Корень контента: к нему привязываются тема и <c>ContentDialog</c>.</summary>
    public FrameworkElement RootElement => Root;

    private void ApplySavedWindowSize()
    {
        var settings = _viewModel.Settings;
        var width = settings.RememberWindowSize ? settings.WindowWidth : WindowPreferences.DefaultWidth;
        var height = settings.RememberWindowSize ? settings.WindowHeight : WindowPreferences.DefaultHeight;
        AppWindow.Resize(new SizeInt32(width, height));
    }

    private void OnWindowSizeResetRequested(object? sender, EventArgs eventArgs)
    {
        AppWindow.Resize(new SizeInt32(_viewModel.Settings.WindowWidth, _viewModel.Settings.WindowHeight));
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs eventArgs)
    {
        if (!eventArgs.DidSizeChange || !_viewModel.Settings.RememberWindowSize)
        {
            return;
        }

        _windowSizeSaveCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _windowSizeSaveCancellation = cancellation;
        _windowSizeSaveTask = SaveWindowSizeAfterDelayAsync(sender, cancellation);
    }

    private async Task SaveWindowSizeAfterDelayAsync(AppWindow window, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(WindowSizeSaveDelay, cancellation.Token);
            var size = window.Size;
            await _viewModel.Settings.SaveWindowSizeAsync(size.Width, size.Height, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_windowSizeSaveCancellation, cancellation))
            {
                _windowSizeSaveCancellation = null;
                _windowSizeSaveTask = null;
            }

            cancellation.Dispose();
        }
    }

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

        // Бургер появляется только после входа: экранам онбординга он ни к чему.
        RootNavigation.IsPaneVisible = viewModel is MessengerViewModel;
        AppTitleBar.IsPaneToggleButtonVisible = viewModel is MessengerViewModel;
        ContentFrame.Navigate(pageType, viewModel, new EntranceNavigationTransitionInfo());
        // Онбординг — линейная цепочка, «назад» в ней не нужно, а стек от неё мешал бы
        // возврату с профиля к мессенджеру.
        ContentFrame.BackStack.Clear();
    }

    /// <summary>
    /// Настройки открываются прямо через <c>Frame</c>, а не через сервис навигации: тот заменяет
    /// текущую ViewModel, и возврат к мессенджеру заново грузил бы список чатов. Профиль —
    /// оверлей поверх мессенджера (см. <see cref="MessengerViewModel.OpenOwnProfileCommand"/>),
    /// а не отдельная страница: бургер показывает пункт «Профиль» только пока CurrentViewModel — MessengerViewModel.
    /// </summary>
    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs eventArgs)
    {
        switch (eventArgs.InvokedItemContainer?.Tag as string)
        {
            case "profile":
                (_viewModel.CurrentViewModel as MessengerViewModel)?.OpenOwnProfileCommand.Execute(null);
                break;
            case "settings" when ContentFrame.CurrentSourcePageType != typeof(SettingsPage):
                ContentFrame.Navigate(typeof(SettingsPage), _viewModel.Settings);
                break;
        }
    }

    private void OnContentFrameNavigated(object sender, NavigationEventArgs eventArgs)
    {
        RootNavigation.IsBackEnabled = ContentFrame.CanGoBack;
        AppTitleBar.IsBackButtonEnabled = ContentFrame.CanGoBack;
    }

    private void OnTitleBarBackRequested(TitleBar sender, object args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void OnTitleBarPaneToggleRequested(TitleBar sender, object args) =>
        RootNavigation.IsPaneOpen = !RootNavigation.IsPaneOpen;

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs eventArgs)
    {
        if (_windowCloseReady)
        {
            return;
        }

        // В режиме MinimizeToTray закрытие отменяется, окно прячется, процесс живёт
        // ради иконки в трее. Пункт «Выход» выставляет флаг и закрывает окно по-настоящему.
        if (!_isExitRequested && _viewModel.Settings.ClosingBehavior == WindowClosingBehavior.MinimizeToTray)
        {
            eventArgs.Cancel = true;
            this.Hide();
            return;
        }

        eventArgs.Cancel = true;
        if (_windowCloseInProgress)
        {
            return;
        }

        _windowCloseInProgress = true;
        _ = CloseAfterSavingWindowSizeAsync();
    }

    private async Task CloseAfterSavingWindowSizeAsync()
    {
        try
        {
            _windowSizeSaveCancellation?.Cancel();
            if (_windowSizeSaveTask is not null)
            {
                await _windowSizeSaveTask;
            }

            if (_viewModel.Settings.RememberWindowSize)
            {
                var size = AppWindow.Size;
                await _viewModel.Settings.SaveWindowSizeAsync(size.Width, size.Height);
            }
        }
        finally
        {
            _windowCloseReady = true;
            _windowCloseInProgress = false;
            Close();
        }
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
