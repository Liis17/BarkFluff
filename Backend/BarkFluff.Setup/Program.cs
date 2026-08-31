using BarkFluff.Setup.Endpoints;
using BarkFluff.Setup.Setup;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;

using System.Net;

namespace BarkFluff.Setup;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var setupOptions = SetupOptions.FromConfiguration(builder.Configuration);

        builder.WebHost.ConfigureKestrel(options =>
            options.ListenAnyIP(setupOptions.Port, listen => listen.Protocols = HttpProtocols.Http1));

        builder.Services.AddSingleton(setupOptions);
        builder.Services.AddSingleton<SetupSessionStore>();
        builder.Services.AddSingleton<SettingsSetupClient>();
        builder.Services.AddSingleton<ISettingsSetupClient>(provider => provider.GetRequiredService<SettingsSetupClient>());
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
            options.KnownProxies.Add(IPAddress.Loopback);
        });

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            await next();
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapSetupEndpoints();

        await app.RunAsync();
    }
}
