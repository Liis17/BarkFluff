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

            // Настройка HTTPS с сертификатом
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(settings.Port, listenOptions =>
                {
                    listenOptions.UseHttps(cert);
                });
            });

            var app = builder.Build();

            app.MapGet("/testping", () => Results.Ok("ok"));

            app.Run();
        }
    }
}
