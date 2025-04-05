using Microsoft.EntityFrameworkCore;
using BarkFluff.Server.CLI.Data;
using System.Security.Cryptography.X509Certificates;

namespace BarkFluff.Server.CLI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Configuration.SetBasePath(Directory.GetCurrentDirectory())
                                 .AddJsonFile("setting/main.json", optional: false, reloadOnChange: true);

            var settings = builder.Configuration.Get<MySettings>();

            var certPath = Path.Combine(Directory.GetCurrentDirectory(), settings.Certificate.Path);
            var cert = new X509Certificate2(certPath, settings.Certificate.Password);

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(settings.Port, listenOptions =>
                {
                    listenOptions.UseHttps(cert);
                });
            });

            builder.Services.AddDbContext<MessengerDbContext>(options =>
                options.UseSqlServer(settings.ConnectionString));

            builder.Services.AddControllers();

            var app = builder.Build();

            app.MapGet("/testping", () => Results.Ok("ok"));
            app.MapControllers();

            app.Run();
        }
    }
}