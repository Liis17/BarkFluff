using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Client.Core.ViewModels.Settings;
using BarkFluff.Client.WinUI.Infrastructure;
using BarkFluff.Client.WinUI.Infrastructure.Appearance;
using BarkFluff.Client.WinUI.Infrastructure.Dialogs;
using BarkFluff.Client.WinUI.Infrastructure.Localization;
using BarkFluff.Client.Core.Services.Calls;
using BarkFluff.Client.WinUI.Infrastructure.Calls;
using BarkFluff.Client.WinUI.Infrastructure.Notifications;
using BarkFluff.Client.WinUI.Infrastructure.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using System.IO;

using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.WinUI;

public partial class App : Application
{
    private static readonly TimeSpan HostShutdownTimeout = TimeSpan.FromSeconds(2);

    private IHost? _host;
    private Window? _window;
    private readonly WindowsNotificationPresenter _notificationPresenter = new();
    private MainWindow? _notificationWindow;
    private readonly Queue<NotificationActivation> _pendingNotifications = new();

    public App()
    {
        InitializeComponent();
        var dispatcher = new DispatcherQueueUiDispatcher();
        _notificationPresenter.Register(activation => dispatcher.Post(() => OnNotificationInvoked(activation)));
        AttachCrashLogging();
    }

    /// <summary>
    /// Контейнер для страниц, на которые переходят из другой страницы: у <c>Page</c> нет
    /// конструктора с зависимостями, а ViewModel такой страницы неоткуда взять параметром.
    /// </summary>
    internal static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            VoiceComposer.CleanTemporaryFiles();
            if (_notificationPresenter.ReadStartupActivation() is { } activation)
                _pendingNotifications.Enqueue(activation);
            _host = CreateHost();
            await _host.StartAsync();

            var services = _host.Services;
            LegacyDatabaseImporter.TryImport(services.GetRequiredService<AppDataPaths>());

            var dataStore = services.GetRequiredService<IApplicationDataStore>();
            await dataStore.InitializeAsync();
            await services.GetRequiredService<OutgoingMessageQueue>().SweepOrphansAsync();

            var theme = await dataStore.GetThemeAsync() ?? ApplicationThemeMode.System;
            var themeService = services.GetRequiredService<IApplicationThemeService>();
            themeService.PrepareAccent(theme);
            if (await dataStore.GetThemeAsync() is null)
            {
                await dataStore.SaveThemeAsync(theme);
            }

            var localization = services.GetRequiredService<ILocalizationService>();
            var language = await dataStore.GetLanguageAsync();
            localization.Apply(localization.ResolveSupportedLanguage(language));

            if (language is null)
            {
                await dataStore.SaveLanguageAsync("system");
            }

            await services.GetRequiredService<SettingsViewModel>().LoadAsync();

            var navigation = services.GetRequiredService<IOnboardingNavigationService>();
            var savedConnection = await dataStore.GetNodeServiceConfigurationAsync();
            var nodeConnectionService = services.GetRequiredService<INodeConnectionService>();
            if (savedConnection is not null && nodeConnectionService.RestoreConnection(savedConnection))
            {
                var authentication = services.GetRequiredService<IAuthenticationService>();
                if (await authentication.TryRestoreSessionAsync())
                {
                    navigation.ShowMessenger();
                }
                else
                {
                    navigation.ShowLogin();
                    if (authentication.RestoreSessionErrorResourceKey is { } errorResourceKey)
                    {
                        services.GetRequiredService<LoginViewModel>().ShowSessionRestrictionNotice(errorResourceKey);
                    }
                }
            }
            else if (await dataStore.HasSeenWelcomeAsync())
            {
                navigation.ShowSelectNode();
            }
            else
            {
                navigation.ShowWelcome();
            }

            if (navigation.CurrentViewModel is not MessengerViewModel)
            {
                _notificationPresenter.RemoveAll();
            }

            var mainWindow = services.GetRequiredService<MainWindow>();
            _window = mainWindow;

            var webApi = services.GetRequiredService<WebApiClient>();
            webApi.TokenInvalidated += (_, _) =>
            {
                var errorResourceKey = webApi.LastTokenRefreshErrorResourceKey;
                if (errorResourceKey is not ("Error_SessionExpired" or "Error_AccountBlocked" or "Error_AccountPendingDeletion"))
                    return;
                var session = services.GetRequiredService<IClientSession>();
                var connection = session.CurrentConnection;
                var userId = connection?.ConnectionParameters.UserId;
                mainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    if (connection is null || userId is not > 0 ||
                        !ReferenceEquals(connection, session.CurrentConnection) ||
                        userId != connection.ConnectionParameters.UserId ||
                        navigation.CurrentViewModel is LoginViewModel)
                        return;
                    var currentView = navigation.CurrentViewModel;
                    var messenger = services.GetRequiredService<MessengerViewModel>();
                    var localCleanup = messenger.ResetAfterSessionRejectionAsync();
                    await services.GetRequiredService<ISecureSessionStore>().ClearAsync();
                    await localCleanup;
                    if (!ReferenceEquals(connection, session.CurrentConnection) ||
                        userId != connection.ConnectionParameters.UserId ||
                        navigation.CurrentViewModel != currentView)
                        return;
                    navigation.ShowRestrictedLogin(errorResourceKey);
                });
            };

            themeService.Attach(mainWindow.RootElement);
            themeService.Apply(theme);
            services.GetRequiredService<IDialogService>().Attach(mainWindow.RootElement.XamlRoot);

            mainWindow.Activate();
            _notificationWindow = mainWindow;
            while (_pendingNotifications.TryDequeue(out var pending))
                OnNotificationInvoked(pending);
        }
        catch (Exception exception)
        {
            ShowStartupFailure(exception);
        }
    }

    /// <summary>
    /// До создания главного окна показывать <c>ContentDialog</c> не на чем — <c>XamlRoot</c>
    /// ещё не существует, поэтому сообщение об ошибке становится содержимым отдельного окна.
    /// </summary>
    private void ShowStartupFailure(Exception exception)
    {
        _ = _notificationPresenter.StopAsync();
        ShutdownHost();

        var window = new Window
        {
            Title = "BarkFluff",
            Content = new TextBlock
            {
                Margin = new Thickness(24),
                TextWrapping = TextWrapping.Wrap,
                Text = exception.Message
            }
        };

        _window = window;
        window.Activate();
    }

    /// <summary>
    /// Исключение в XAML-колбэке (материализация шаблона, конвертер, обработчик события)
    /// без обработчика превращается в нативный stowed exception 0xC000027B: процесс падает
    /// внутри Microsoft.UI.Xaml.dll, а отладчик остаётся без управляемого стека. Обработчики
    /// только пишут причину в файл и не гасят исключение, чтобы падение оставалось видимым.
    /// </summary>
    private void AttachCrashLogging()
    {
        UnhandledException += (_, eventArgs) => LogCrash("Xaml", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) => LogCrash("AppDomain", eventArgs.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) => LogCrash("Task", eventArgs.Exception);

        DebugSettings.IsBindingTracingEnabled = true;
        DebugSettings.BindingFailed += (_, eventArgs) => LogLine($"BindingFailed: {eventArgs.Message}");
    }

    private static void LogCrash(string source, Exception? exception) =>
        LogLine($"{source}: {exception}");

    private static void LogLine(string message)
    {
        try
        {
            var directory = AppDataPaths.CreateDefault().DataDirectory;
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Останавливает host, не давая процессу зависнуть. Контейнер держит сервисы с
    /// <see cref="IAsyncDisposable"/> и gRPC-каналы: закрытие канала с живым server-стримом
    /// может не вернуться, а блокирующее ожидание на UI-потоке в этом случае не даёт процессу
    /// завершиться совсем. Поэтому теардаун выполняется вне UI-потока и с ограничением по времени.
    /// </summary>
    private void ShutdownHost()
    {
        var host = _host;
        _host = null;
        if (host is null)
        {
            return;
        }

        var teardown = Task.Run(async () =>
        {
            await host.StopAsync();
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        });

        teardown.Wait(HostShutdownTimeout);
    }

    private void OnNotificationInvoked(NotificationActivation activation)
    {
        if (_notificationWindow is not { } window)
        {
            _pendingNotifications.Enqueue(activation);
            return;
        }
        if (activation.Reply is { } reply)
            _ = ReplyToNotificationAsync(reply, Services);
        else
            _ = window.OpenNotificationAsync(activation.Target);
    }

    private async Task ReplyToNotificationAsync(NotificationReply reply, IServiceProvider services)
    {
        var navigation = services.GetRequiredService<IOnboardingNavigationService>();
        var error = navigation.CurrentViewModel is MessengerViewModel messenger
            ? await messenger.ReplyToNotificationAsync(reply)
            : "Notifications_ReplySessionMismatch";
        if (error is not null && _notificationWindow is not null)
            _notificationPresenter.ShowReplyError(services.GetRequiredService<ILocalizationService>().GetString(error));
    }

    internal void ShowCallNotification(Guid id, string title) => _notificationPresenter.ShowCall(id, title);

    internal void RemoveCallNotification(Guid id) => _notificationPresenter.RemoveCall(id);

    internal Task StopNotificationsAsync()
    {
        _notificationWindow = null;
        return _notificationPresenter.StopAsync();
    }

    private IHost CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton(AppDataPaths.CreateDefault());
        builder.Services.AddSingleton<SqliteApplicationDataStore>();
        builder.Services.AddSingleton<IApplicationDataStore>(services => services.GetRequiredService<SqliteApplicationDataStore>());
        builder.Services.AddSingleton<IChatListCacheStore>(services => services.GetRequiredService<SqliteApplicationDataStore>());
        builder.Services.AddSingleton<IMessageHistoryCacheStore>(services => services.GetRequiredService<SqliteApplicationDataStore>());
        builder.Services.AddSingleton<MessageHistoryCache>();
        builder.Services.AddSingleton<ChatListCache>();
        builder.Services.AddSingleton<IChatDraftStore>(services => services.GetRequiredService<SqliteApplicationDataStore>());
        builder.Services.AddSingleton<IChatDraftRemote, ChatDraftRemote>();
        builder.Services.AddSingleton<ISecureSessionStore, DpapiSecureSessionStore>();
        builder.Services.AddSingleton<IPrivateChatKeyStore, DpapiPrivateChatKeyStore>();
        builder.Services.AddSingleton<IRealtimeMessengerService, RealtimeMessengerService>();
        builder.Services.AddSingleton<IOnlinePresenceService, OnlinePresenceService>();
        builder.Services.AddSingleton<IChatTypingService, ChatTypingService>();
        builder.Services.AddSingleton<IApplicationThemeService, ApplicationThemeService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<IUiDispatcher, DispatcherQueueUiDispatcher>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<WebApiClient>();
        builder.Services.AddSingleton<INodeAddressParser, NodeAddressParser>();
        builder.Services.AddSingleton<IClientSession, ClientSession>();
        builder.Services.AddSingleton<MessengerService>();
        builder.Services.AddSingleton<IMessengerService>(services => services.GetRequiredService<MessengerService>());
        builder.Services.AddSingleton<IOutgoingMessageTransport>(services => services.GetRequiredService<MessengerService>());
        builder.Services.AddSingleton<IOutgoingMessageStore>(services => services.GetRequiredService<SqliteApplicationDataStore>());
        builder.Services.AddSingleton(services => new OutgoingMessageQueue(
            services.GetRequiredService<IOutgoingMessageStore>(), services.GetRequiredService<IOutgoingMessageTransport>(),
            services.GetRequiredService<AppDataPaths>(), TimeProvider.System));
        builder.Services.AddSingleton<IStickerCatalog, StickerCatalog>();
        builder.Services.AddSingleton(_ => Controls.StickerImage.CreateCache());
        builder.Services.AddSingleton<StickerPickerViewModel>();
        builder.Services.AddSingleton<INodeConnectionService, NodeConnectionService>();
        builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
        builder.Services.AddSingleton<IOnboardingNavigationService, OnboardingNavigationService>();
        builder.Services.AddSingleton<ISecuritySettingsService, SecuritySettingsService>();
        builder.Services.AddSingleton<IAccountSettingsService, AccountSettingsService>();
        builder.Services.AddSingleton<IUserPreferencesService, UserPreferencesService>();
        builder.Services.AddSingleton<IChatFoldersState, ChatFoldersState>();
        builder.Services.AddSingleton<IDeviceSettingsService, DeviceSettingsService>();
        builder.Services.AddSingleton<INotificationPresenter>(_notificationPresenter);
        builder.Services.AddSingleton<MessageNotificationService>();
        builder.Services.AddSingleton<ISettingsPreferences, SettingsPreferences>();
        builder.Services.AddSingleton<IAboutSettingsService, AboutSettingsService>();
        builder.Services.AddSingleton<HttpClient>(_ => UpdateHttpClientFactory.Create());
        builder.Services.AddSingleton<IUpdateService, UpdateService>();

        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<WelcomeViewModel>();
        builder.Services.AddSingleton<SelectNodeViewModel>();
        builder.Services.AddSingleton<ConnectedNodeViewModel>();
        builder.Services.AddSingleton<LoginViewModel>();
        builder.Services.AddSingleton<RegistrationViewModel>();
        builder.Services.AddSingleton<PasswordRecoveryViewModel>();
        builder.Services.AddSingleton<IVoiceRecorder, WindowsVoiceRecorder>();
        builder.Services.AddSingleton<IVoicePlayer, WindowsVoicePlayer>();
        builder.Services.AddSingleton<WebViewCallMedia>();
        builder.Services.AddSingleton<ICallMedia>(services => services.GetRequiredService<WebViewCallMedia>());
        builder.Services.AddSingleton<CallSignaling>();
        builder.Services.AddSingleton(services => new AudioCallService(services.GetRequiredService<ICallMedia>(),
            services.GetRequiredService<IUiDispatcher>(), TimeProvider.System));
        builder.Services.AddSingleton<MessengerViewModel>();
        builder.Services.AddSingleton<ProfileViewModel>();
        builder.Services.AddSingleton<SettingsSecurityViewModel>();
        builder.Services.AddSingleton<SettingsAccountViewModel>();
        builder.Services.AddSingleton<SettingsPrivacyViewModel>();
        builder.Services.AddSingleton<SettingsNotificationsViewModel>();
        builder.Services.AddSingleton<SettingsDevicesViewModel>();
        builder.Services.AddSingleton<SettingsStorageViewModel>();
        builder.Services.AddSingleton<SettingsPersonalizationViewModel>();
        builder.Services.AddTransient<SettingsChatFoldersViewModel>();
        builder.Services.AddSingleton<SettingsLanguageViewModel>();
        builder.Services.AddSingleton<SettingsUpdateViewModel>();
        builder.Services.AddSingleton<SettingsAboutViewModel>();
        builder.Services.AddSingleton<SettingsTestingViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
