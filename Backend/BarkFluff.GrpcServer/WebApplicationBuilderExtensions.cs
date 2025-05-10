using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;
using Grpc.Core;
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
    {
        const string ConfigurationsAddress = "http://192.168.1.111:7003";
        
        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        
        var channel = GrpcChannel.ForAddress(ConfigurationsAddress);
        var configurationApiClient = new ConfigurationApi.ConfigurationApiClient(channel);

        var config = configurationApiClient.GetConfiguration(new GetConfigurationRequest {ServiceId = (int)serviceId});
        
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
        
        configurationDictionary.Add("ConfigurationServiceAddr", ConfigurationsAddress);
        
        builder.Configuration.AddInMemoryCollection(configurationDictionary);
        
        return builder;
    }
}