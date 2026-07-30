using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.ClientV2.WPF.Infrastructure.Appearance;
using BarkFluff.ClientV2.WPF.Infrastructure.Storage;
using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.ClientV2.WPF.ViewModels;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using System.Windows;

namespace BarkFluff.ClientV2.WPF;

public partial class App : Application
{
    private static readonly TimeSpan HostShutdownTimeout = TimeSpan.FromSeconds(2);

    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = CreateHost();
            await _host.StartAsync();

            var services = _host.Services;
            var dataStore = services.GetRequiredService<IApplicationDataStore>();
            await dataStore.InitializeAsync();

            var theme = await dataStore.GetThemeAsync() ?? Models.ApplicationThemeMode.System;
            services.GetRequiredService<IApplicationThemeService>().Apply(theme);
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
            mainWindow.Show();
            services.GetRequiredService<IApplicationThemeService>().WatchSystemTheme(mainWindow, theme);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "BarkFluff",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ShutdownHost();
        base.OnExit(e);
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

        builder.Services.AddSingleton(new AppDataPaths(AppContext.BaseDirectory));
        builder.Services.AddSingleton<IApplicationDataStore, SqliteApplicationDataStore>();
        builder.Services.AddSingleton<ISecureSessionStore, DpapiSecureSessionStore>();
        builder.Services.AddSingleton<IPrivateChatKeyStore, DpapiPrivateChatKeyStore>();
        builder.Services.AddSingleton<IRealtimeMessengerService, RealtimeMessengerService>();
        builder.Services.AddSingleton<IApplicationThemeService, ApplicationThemeService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<WebApiClient>();
        builder.Services.AddSingleton<INodeAddressParser, NodeAddressParser>();
        builder.Services.AddSingleton<IClientSession, ClientSession>();
        builder.Services.AddSingleton<IMessengerService, MessengerService>();
        builder.Services.AddSingleton<INodeConnectionService, NodeConnectionService>();
        builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
        builder.Services.AddSingleton<IOnboardingNavigationService, OnboardingNavigationService>();

        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<WelcomeViewModel>();
        builder.Services.AddSingleton<SelectNodeViewModel>();
        builder.Services.AddSingleton<ConnectedNodeViewModel>();
        builder.Services.AddSingleton<LoginViewModel>();
        builder.Services.AddSingleton<RegistrationViewModel>();
        builder.Services.AddSingleton<PasswordRecoveryViewModel>();
        builder.Services.AddSingleton<MessengerViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
