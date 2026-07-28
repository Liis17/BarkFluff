using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.ClientV2.WPF.Infrastructure.Storage;
using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.ClientV2.WPF.ViewModels;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using System.Windows;
using Wpf.Ui.Appearance;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace BarkFluff.ClientV2.WPF;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            ApplicationThemeManager.Apply(
                ApplicationTheme.Light,
                WindowBackdropType.Mica,
                updateAccent: true);

            _host = CreateHost();
            await _host.StartAsync();

            var services = _host.Services;
            var dataStore = services.GetRequiredService<IApplicationDataStore>();
            await dataStore.InitializeAsync();

            var localization = services.GetRequiredService<ILocalizationService>();
            var language = await dataStore.GetLanguageAsync();
            language = localization.ResolveSupportedLanguage(language);
            localization.Apply(language);

            if (await dataStore.GetLanguageAsync() is null)
            {
                await dataStore.SaveLanguageAsync(language);
            }

            var navigation = services.GetRequiredService<IOnboardingNavigationService>();
            if (await dataStore.HasSeenWelcomeAsync())
            {
                navigation.ShowSelectNode();
            }
            else
            {
                navigation.ShowWelcome();
            }

            services.GetRequiredService<MainWindow>().Show();
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
        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static IHost CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton(new AppDataPaths(AppContext.BaseDirectory));
        builder.Services.AddSingleton<IApplicationDataStore, SqliteApplicationDataStore>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<WebApiClient>();
        builder.Services.AddSingleton<INodeAddressParser, NodeAddressParser>();
        builder.Services.AddSingleton<IClientSession, ClientSession>();
        builder.Services.AddSingleton<INodeConnectionService, NodeConnectionService>();
        builder.Services.AddSingleton<IOnboardingNavigationService, OnboardingNavigationService>();

        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<WelcomeViewModel>();
        builder.Services.AddSingleton<SelectNodeViewModel>();
        builder.Services.AddSingleton<ConnectedNodeViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
