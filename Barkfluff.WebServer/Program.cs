using Barkfluff.WebServer.Services;

namespace Barkfluff.WebServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ListenAnyIP(64641);
            });

            builder.Services.AddControllers();

            builder.Services.AddSingleton<UserPageService>();

            var app = builder.Build();

            app.UseRouting();

            app.MapControllers();

            app.Run();
        }
    }
}
