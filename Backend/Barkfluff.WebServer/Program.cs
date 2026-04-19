using Barkfluff.WebServer.Services;

using BarkFluff.Proto.Users;

using Grpc.Net.Client;

namespace Barkfluff.WebServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                var port = int.Parse(builder.Configuration["WEBSERVER_PORT"] ?? "64641");
                serverOptions.ListenAnyIP(port);
            });

            builder.Services.AddControllers();

            // === Сервисы ===
            builder.Services.AddSingleton<LegalPageService>();

            // === gRPC: подключение к Users-сервису бекенда ===
            var usersServiceHost = builder.Configuration["UsersService:Host"]!;
            var usersServiceToken = builder.Configuration["UsersService:Token"]!;

            var usersChannel = GrpcChannel.ForAddress(usersServiceHost);
            builder.Services.AddSingleton(new UsersServerApi.UsersServerApiClient(usersChannel));

            // === Кеширование профилей пользователей (30 минут) ===
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton(sp => new UserProfileService(
                sp.GetRequiredService<UsersServerApi.UsersServerApiClient>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                usersServiceToken,
                sp.GetRequiredService<ILogger<UserProfileService>>()));

            builder.Services.AddSingleton<UserPageService>();
            builder.Services.AddSingleton<SupportChatService>();
            builder.Services.AddSingleton<TelegramService>(sp =>
            {
                var chatService = sp.GetRequiredService<SupportChatService>();
                var logger = sp.GetRequiredService<ILogger<TelegramService>>();
                var token = builder.Configuration["Telegram:BotToken"]!;
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
