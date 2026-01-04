using Barkfluff.WebServer.Services;

namespace Barkfluff.WebServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure Kestrel to listen on port 64641
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ListenAnyIP(64641);
            });

            // Add services to the container.
            builder.Services.AddControllers();
            
            // Register UserPageService
            builder.Services.AddSingleton<UserPageService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            app.UseRouting();

            // Map controllers with proper order
            // Specific routes will be matched first, fallback last
            app.MapControllers();

            app.Run();
        }
    }
}
