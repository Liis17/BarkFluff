using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
namespace DockerServerManagement
{
    public class Program
    {
        private static List<long> AllowedUserIds = new List<long>();
        static async Task Main()
        {
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, ".env")))
            {
                File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, ".env"), new string[]
                {
                    $"TELEGRAM_TOKEN=",
                    $"ALLOWED_USER_ID="
                });
                return;
            }
            var envLines = await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, ".env"));
            var telegramToken = envLines.FirstOrDefault(line => line.StartsWith("TELEGRAM_TOKEN="))?.Split('=')[1];
            AllowedUserIds = envLines.Length > 1 ? envLines.FirstOrDefault(line => line.StartsWith("ALLOWED_USER_ID="))?.Split('=')[1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(idStr => long.TryParse(idStr, out var id) ? id : (long?)null)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToList() ?? new List<long>() : new List<long>();

            var botClient = new TelegramBotClient(telegramToken);

            var me = await botClient.GetMe();
            Console.WriteLine($"Бот запущен: {me.Username}");

            var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
            botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions);

            Console.ReadLine();
        }

        static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message != null)
            {
                var message = update.Message;
                if (!AllowedUserIds.Contains(message.From.Id)) return;

                if (message.Text == "/start")
                {
                    await SendMainMenu(botClient, message.Chat.Id);
                }
            }
            else if (update.CallbackQuery != null)
            {
                var callbackQuery = update.CallbackQuery;
                if (!AllowedUserIds.Contains(callbackQuery.From.Id)) return;

                if (callbackQuery.Data == "status")
                {
                    await HandleStatus(botClient, callbackQuery.Message.Chat.Id);
                }
                else if (callbackQuery.Data == "update")
                {
                    await HandleUpdate(botClient, callbackQuery.Message.Chat.Id);
                }

                await botClient.AnswerCallbackQuery(callbackQuery.Id);
            }
        }

        static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception is ApiRequestException apiRequestException
                ? $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}"
                : exception.ToString();

            Console.WriteLine(errorMessage);
            return Task.CompletedTask;
        }

        static async Task SendMainMenu(ITelegramBotClient botClient, long chatId)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
            new [] { InlineKeyboardButton.WithCallbackData("Статус", "status") },
            new [] { InlineKeyboardButton.WithCallbackData("Обновить", "update") }
        });

            await botClient.SendMessage(chatId, "Главное меню", replyMarkup: inlineKeyboard);
        }

        static async Task HandleStatus(ITelegramBotClient botClient, long chatId)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "compose -p barkfluff ps --format json",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            var containers = output.Trim('[', ']').Split("},{")
                .Select(s => s.Trim('{', '}', '"'))
                .Select(s => s.Split(',').ToDictionary(kv => kv.Split(':')[0].Trim('"'), kv => kv.Split(':')[1].Trim('"')))
                .ToList();

            string statusMessage = "Статус контейнеров:\n";
            foreach (var container in containers)
            {
                statusMessage += $"Имя: {container.GetValueOrDefault("Name", "N/A")}, Состояние: {container.GetValueOrDefault("State", "N/A")}\n";
            }

            await botClient.SendMessage(chatId, statusMessage);
        }

        static async Task HandleUpdate(ITelegramBotClient botClient, long chatId)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c update.cmd",
                    WorkingDirectory = @"C:\BarkFluff",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            await botClient.SendMessage(chatId, $"Обновление выполнено. Вывод: {output}");
        }
    }
}
