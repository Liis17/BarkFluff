using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

using Grpc.Net.Client;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.GrpcServer;

public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder SetRunningAddress(this WebApplicationBuilder builder,
        IConfiguration configuration)
    {
        builder.Services.AddSettings<RunSettings>(configuration, "RunSettings");

        var runSettings = configuration.GetSection("RunSettings").Get<RunSettings>();

        // Проверяем, что порт задан корректно
        if (runSettings == null || runSettings.Port <= 0)
        {
            var portValue = configuration["RunSettings:Port"];
            throw new InvalidOperationException(
                $"Некорректное значение порта в конфигурации. " +
                $"RunSettings:Port = '{portValue}'. " +
                $"Ожидается числовое значение (например, 7009). " +
                $"Проверьте переменные окружения Docker или файл appsettings.json");
        }

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(runSettings.Port, listenOptions =>
            {
                if (runSettings.Tls != null)
                {
                    listenOptions.UseHttps(runSettings.Tls.Filename, runSettings.Tls.Password);
                }
                listenOptions.Protocols = HttpProtocols.Http2;
            });

            if (runSettings.Http1Port != null)
            {
                options.ListenAnyIP(runSettings.Http1Port.Value, listenOptions =>
                {
                    if (runSettings.Tls != null)
                    {
                        listenOptions.UseHttps(runSettings.Tls.Filename, runSettings.Tls.Password);
                    }
                    listenOptions.Protocols = HttpProtocols.Http1;
                });
            }
        });

        return builder;
    }

    public static WebApplicationBuilder LoadConfiguration(this WebApplicationBuilder builder, ServiceId serviceId)
        => builder.LoadConfiguration(serviceId, requireValidSettings: true);

    public static WebApplicationBuilder LoadConfiguration(this WebApplicationBuilder builder, ServiceId serviceId,
        bool requireValidSettings)
    {
        var consoleOutput = BarkFluffLoggingOptions.ResolveConsoleOutput(builder.Configuration);
        var configurationServiceAddress = Environment.GetEnvironmentVariable("SETTINGS_SERVICE_URL")
                                          ?? Environment.GetEnvironmentVariable("CONFIGURATION_SERVICE_URL")
                                          ?? builder.Configuration["SettingsServiceAddr"]
                                          ?? builder.Configuration["ConfigurationServiceAddr"]
                                          ?? "http://localhost:7003";

        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        using var channel = GrpcChannel.ForAddress(configurationServiceAddress);
        var configurationApiClient = new ConfigurationApi.ConfigurationApiClient(channel);

        using var readinessClient = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(configurationServiceAddress.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        var config = ConfigurationStartupLoader.LoadAsync(readinessClient,
            () => configurationApiClient.GetConfigurationAsync(
                new GetConfigurationRequest { ServiceId = (int)serviceId },
                deadline: DateTime.UtcNow.AddSeconds(5)).ResponseAsync,
            requireValidSettings,
            consoleOutput: consoleOutput).GetAwaiter().GetResult();

        var configurationDictionary = new Dictionary<string, string>();

        foreach (var configurationItem in config.Configurations)
        {
            var key = configurationItem.Section;

            if (!string.IsNullOrWhiteSpace(configurationItem.Key))
            {
                key += $":{configurationItem.Key}";
            }

            configurationDictionary.Add(key, configurationItem.Value);
        }

        configurationDictionary.Add("SettingsServiceAddr", configurationServiceAddress);
        configurationDictionary.Add("ConfigurationServiceAddr", configurationServiceAddress);

        builder.Configuration.AddInMemoryCollection(configurationDictionary);

        return builder;
    }
}
