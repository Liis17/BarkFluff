using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Client.Core.ViewModels.Settings;
using BarkFluff.Client.WinUI.Infrastructure.Appearance;
using BarkFluff.Client.WinUI.Infrastructure.Dialogs;
using BarkFluff.Client.WinUI.Infrastructure.Localization;
using BarkFluff.Client.WinUI.Infrastructure.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.WinUI;

public partial class App : Application
{
    private static readonly TimeSpan HostShutdownTimeout = TimeSpan.FromSeconds(2);

    private IHost? _host;
    private Window? _window;

    public App() => InitializeComponent();

    /// <summary>
    /// Контейнер для страниц, на которые переходят из другой страницы: у <c>Page</c> нет
    /// конструктора с зависимостями, а ViewModel такой страницы неоткуда взять параметром.
    /// </summary>
    internal static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _host = CreateHost();
            await _host.StartAsync();

            var services = _host.Services;
            LegacyDatabaseImporter.TryImport(services.GetRequiredService<AppDataPaths>());

            var dataStore = services.GetRequiredService<IApplicationDataStore>();
            await dataStore.InitializeAsync();

            var theme = await dataStore.GetThemeAsync() ?? ApplicationThemeMode.System;
            var themeService = services.GetRequiredService<IApplicationThemeService>();
            themeService.PrepareAccent(theme);
            if (await dataStore.GetThemeAsync() is null)
            {
                await dataStore.SaveThemeAsync(theme);
            }

            var localization = services.GetRequiredService<ILocalizationService>();
            var language = await dataStore.GetLanguageAsync();
            language = localization.ResolveSupportedLanguage(language);
            localization.Apply(language);

            if (await dataStore.GetLanguageAsync() is null)
            {
                await dataStore.SaveLanguageAsync(language);
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

            var mainWindow = services.GetRequiredService<MainWindow>();
            _window = mainWindow;

            themeService.Attach(mainWindow.RootElement);
            themeService.Apply(theme);
            services.GetRequiredService<IDialogService>().Attach(mainWindow.RootElement.XamlRoot);

            mainWindow.Activate();
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

    private static IHost CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton(AppDataPaths.CreateDefault());
        builder.Services.AddSingleton<IApplicationDataStore, SqliteApplicationDataStore>();
        builder.Services.AddSingleton<ISecureSessionStore, DpapiSecureSessionStore>();
        builder.Services.AddSingleton<IPrivateChatKeyStore, DpapiPrivateChatKeyStore>();
        builder.Services.AddSingleton<IRealtimeMessengerService, RealtimeMessengerService>();
        builder.Services.AddSingleton<IOnlinePresenceService, OnlinePresenceService>();
        builder.Services.AddSingleton<IApplicationThemeService, ApplicationThemeService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<IUiDispatcher, DispatcherQueueUiDispatcher>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<WebApiClient>();
        builder.Services.AddSingleton<INodeAddressParser, NodeAddressParser>();
        builder.Services.AddSingleton<IClientSession, ClientSession>();
        builder.Services.AddSingleton<IMessengerService, MessengerService>();
        builder.Services.AddSingleton<INodeConnectionService, NodeConnectionService>();
        builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
        builder.Services.AddSingleton<IOnboardingNavigationService, OnboardingNavigationService>();
        builder.Services.AddSingleton<ISecuritySettingsService, SecuritySettingsService>();

        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<WelcomeViewModel>();
        builder.Services.AddSingleton<SelectNodeViewModel>();
        builder.Services.AddSingleton<ConnectedNodeViewModel>();
        builder.Services.AddSingleton<LoginViewModel>();
        builder.Services.AddSingleton<RegistrationViewModel>();
        builder.Services.AddSingleton<PasswordRecoveryViewModel>();
        builder.Services.AddSingleton<MessengerViewModel>();
        builder.Services.AddSingleton<ProfileViewModel>();
        builder.Services.AddSingleton<SettingsSecurityViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
