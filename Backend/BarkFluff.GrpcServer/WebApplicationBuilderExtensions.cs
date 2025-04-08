using BarkFluff.GrpcServer.Settings;
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
        });

        return builder;
    }
}