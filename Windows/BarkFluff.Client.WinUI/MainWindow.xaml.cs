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

namespace BarkFluff.Client.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _isExitRequested;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
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

        // Бургер появляется только после входа: экранам онбординга он ни к чему.
        RootNavigation.IsPaneVisible = viewModel is MessengerViewModel;
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

    private void OnContentFrameNavigated(object sender, NavigationEventArgs eventArgs) =>
        RootNavigation.IsBackEnabled = ContentFrame.CanGoBack;

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs eventArgs)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
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
