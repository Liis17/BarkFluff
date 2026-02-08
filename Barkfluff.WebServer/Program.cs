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
            builder.Services.AddSingleton<SupportChatService>();
            builder.Services.AddSingleton<TelegramService>(sp =>
            {
                var chatService = sp.GetRequiredService<SupportChatService>();
                var logger = sp.GetRequiredService<ILogger<TelegramService>>();
                var token = "8190478937:AAHjtPACmQ5LcbC9Q2McDdnlJH-Sz3XQYJQ";
                return new TelegramService(token, chatService, logger);
            });

            var app = builder.Build();

            app.UseRouting();

            app.MapControllers();

            var telegramService = app.Services.GetRequiredService<TelegramService>();
            telegramService.Start();

            app.Run();
        }
    }
}
