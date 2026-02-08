
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

namespace Barkfluff.WebServer.Services
{
    public class TelegramService
    {
        private readonly TelegramBotClient botClient;
        private long OwnerID = 495716470;
        private string botUsername;

        // Словарь для хранения групп сообщений
        private static readonly Dictionary<string, List<Message>> _mediaGroups = new();
        private static readonly Dictionary<string, Timer> _groupTimers = new();
        // Конструктор принимает токен бота
        public TelegramService(string token)
        {
            botClient = new TelegramBotClient(token);
        }

        // Метод запуска бота
        public void Start()
        {
            botClient.StartReceiving(
                HandleUpdateAsync, // Обработчик обновлений
                HandleErrorAsync   // Обработчик ошибок
            );
            var me = botClient.GetMe;
            var info = me.Invoke();
            botUsername = info.Result.Username;
        }

        // Асинхронный обработчик всех обновлений от Telegram
        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == UpdateType.Message)
            {
                var message = update.Message;
                // Проверяем, есть ли пользователь в базе
                if (!Program.DataBase.IsUserAllowedId(message.Chat.Id))
                {
                    var inlineKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new [] { InlineKeyboardButton.WithCallbackData("Запрос доступа", $"AccessRequest:{message.Chat.Id}") }
                    });
                    await botClient.SendMessage(message.Chat.Id, "У тебя нет доступа!", replyMarkup: inlineKeyboard);
                    return;
                }

                if (message.Text != null)
                {
                    switch (message.Text)
                    {
                        case "/start":
                            await SendStartMessage(message.Chat.Id); // Отправка стартового сообщения с кнопками
                            break;
                        case "Option 1":
                            await botClient.SendMessage(message.Chat.Id, "Вы выбрали Option 1");
                            break;
                        case "Option 2":
                            await botClient.SendMessage(message.Chat.Id, "Вы выбрали Option 2");
                            break;
                        // Добавьте сюда свои команды
                        default:
                            await botClient.SendMessage(message.Chat.Id, "Неизвестная команда. Попробуйте /start или /search");
                            break;
                    }
                }
                else if (message.Photo != null) // Если сообщение содержит фото
                {
                    await HandlePhotoMessageAsync(message);
                }
            }
            else if (update.Type == UpdateType.CallbackQuery) // Обработка нажатий на inline-кнопки
            {
                await HandleCallbackQuery(update.CallbackQuery);
            }
            else if (update.Type == UpdateType.InlineQuery) // Обработка inline-запросов
            {
                await HandleInlineQuery(botClient, update.InlineQuery);
            }
        }

        // Обработка сообщения с фотографиями
        private async Task HandlePhotoMessageAsync(Message message)
        {
            // Если сообщение является частью медиагруппы
            if (!string.IsNullOrEmpty(message.MediaGroupId))
            {
                await HandleMediaGroupPhotoAsync(message);
                return;
            }

            // Обработка одиночной фотографии
            await ProcessSinglePhoto(message);
        }
        private async Task HandleMediaGroupPhotoAsync(Message message)
        {
            var groupId = message.MediaGroupId;

            lock (_mediaGroups)
            {
                // Добавляем сообщение в группу
                if (!_mediaGroups.ContainsKey(groupId))
                {
                    _mediaGroups[groupId] = new List<Message>();
                }
                _mediaGroups[groupId].Add(message);

                // Отменяем предыдущий таймер если он был
                if (_groupTimers.ContainsKey(groupId))
                {
                    _groupTimers[groupId].Dispose();
                }

                // Создаем таймер на 2 секунды для обработки группы
                _groupTimers[groupId] = new Timer(async _ =>
                {
                    try
                    {
                        await ProcessMediaGroup(groupId);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при обработке медиагруппы {groupId}: {ex.Message}");
                    }
                }, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
            }
        }

        // Обработка группы медиафайлов
        private async Task ProcessMediaGroup(string groupId)
        {
            List<Message> groupMessages;

            lock (_mediaGroups)
            {
                if (!_mediaGroups.TryGetValue(groupId, out groupMessages))
                    return;

                // Удаляем группу из словарей
                _mediaGroups.Remove(groupId);
                if (_groupTimers.ContainsKey(groupId))
                {
                    _groupTimers[groupId].Dispose();
                    _groupTimers.Remove(groupId);
                }
            }

            // Находим сообщение с caption (кодом товара)
            var messageWithCaption = groupMessages.FirstOrDefault(m => !string.IsNullOrEmpty(m.Caption));

            if (messageWithCaption == null || !int.TryParse(messageWithCaption.Caption, out int code))
            {
                await botClient.SendMessage(
                    chatId: groupMessages.First().Chat.Id,
                    text: "Пожалуйста, укажите числовой код товара в подписи к одной из фотографий."
                );
                return;
            }

            // Проверяем, есть ли товар в базе
            var item = Program.DataBase.GetItemById(code);
            if (item == null)
            {
                await botClient.SendMessage(
                    chatId: groupMessages.First().Chat.Id,
                    text: $"Товар с кодом {code} не найден в базе."
                );
                return;
            }

            int successCount = 0;
            int totalCount = groupMessages.Count;

            // Обрабатываем все фотографии в группе
            foreach (var msg in groupMessages)
            {
                try
                {
                    var result = await ProcessPhotoForItem(msg, code, item, messageWithCaption.From.Id.ToString());
                    if (result)
                        successCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при обработке фотографии из группы: {ex.Message}");
                }
            }

            // Отправляем результат
            if (successCount > 0)
            {
                await botClient.SendMessage(
                    chatId: groupMessages.First().Chat.Id,
                    text: $"Успешно загружено {successCount} из {totalCount} фотографий для товара с кодом {code}."
                );
            }
            else
            {
                await botClient.SendMessage(
                    chatId: groupMessages.First().Chat.Id,
                    text: $"Не удалось загрузить ни одной фотографии для товара с кодом {code}."
                );
            }
        }

        // Обработка одиночной фотографии
        private async Task ProcessSinglePhoto(Message message)
        {
            // Проверяем наличие подписи (кода)
            if (string.IsNullOrEmpty(message.Caption) || !int.TryParse(message.Caption, out int code))
            {
                await botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Пожалуйста, укажите числовой код товара в подписи к фотографии."
                );
                return;
            }

            // Проверяем, есть ли товар в базе
            var item = Program.DataBase.GetItemById(code);
            if (item == null)
            {
                await botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"Товар с кодом {code} не найден в базе."
                );
                return;
            }

            try
            {
                var result = await ProcessPhotoForItem(message, code, item, message.From.Id.ToString());

                if (result)
                {
                    await botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: $"Фотография для товара с кодом {code} успешно загружена."
                    );
                }
                else
                {
                    await botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: $"Не удалось загрузить фотографию для товара с кодом {code}."
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке фотографии: {ex.Message}");
                await botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"Ошибка при загрузке фотографии для товара с кодом {code}: {ex.Message}"
                );
            }
        }

        // Общий метод обработки фотографии для товара
        private async Task<bool> ProcessPhotoForItem(Message message, int code, object item, string userId)
        {
            try
            {
                // Берем только самую большую фотографию (высокого качества)
                var highestQualityPhoto = message.Photo
                    .OrderByDescending(p => p.FileSize ?? 0)
                    .First();

                // Получаем Bitmap из фотографии
                var bitmap = await GetBitmapFromPhotoAsync(highestQualityPhoto.FileId);
                if (bitmap == null)
                {
                    Console.WriteLine("Не удалось получить bitmap для фотографии");
                    return false;
                }

                // Генерируем GUID для изображения и превью
                var imageGuid = Guid.NewGuid().ToString();
                var previewGuid = Guid.NewGuid().ToString();

                try
                {
                    // Загружаем в MinIO
                    await Program.Minio.UploadBitmapAsync(imageGuid, bitmap, MinioService.bucketNames.Images);
                    await Program.Minio.UploadBitmapAsync(previewGuid, bitmap, MinioService.bucketNames.ImagesPreview);

                    // Сохраняем информацию в базе
                    Program.DataBase.AddImage(code, imageGuid, previewGuid, userId);

                    Console.WriteLine($"Фотография успешно обработана (GUID: {imageGuid})");
                    return true;
                }
                finally
                {
                    // Освобождаем ресурсы
                    bitmap.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в ProcessPhotoForItem: {ex.Message}");
                return false;
            }
        }
        // Метод для преобразования фото в Bitmap
        private async Task<Bitmap> GetBitmapFromPhotoAsync(string fileId)
        {
            try
            {
                var file = await botClient.GetFile(fileId);
                if (file == null || string.IsNullOrEmpty(file.FilePath))
                {
                    Console.WriteLine($"Не удалось получить файл для FileId: {fileId}");
                    return null;
                }

                using var stream = new MemoryStream();
                await botClient.DownloadFile(file.FilePath, stream);
                stream.Position = 0;

                using var bitmap = new Bitmap(stream);
                return new Bitmap(bitmap); // Создаем копию Bitmap
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при преобразовании фото в Bitmap (FileId: {fileId}): {ex.Message}");
                return null;
            }
        }
        // Метод для отправки стартового сообщения с кнопками
        private async Task SendStartMessage(long chatId)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new [] { InlineKeyboardButton.WithWebApp("Кнопка 1", new WebAppInfo { Url = "WEB_APP_URL" }) },
                new [] { InlineKeyboardButton.WithCallbackData("Кнопка 2", "btn_2") }
            });
            await botClient.SendMessage(chatId, "Выберите опцию:", replyMarkup: inlineKeyboard);

            var OpenAppSearchKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithWebApp("Открыть панель поиска", new WebAppInfo
                    {
                        Url = $"{WEB_APP_URL}/search"
                    })
                }
            });

            await botClient.SendMessage(chatId, "Выберите опцию:", replyMarkup: OpenAppSearchKeyboard);
            await botClient.SendMessage(chatId, "Или выберите ниже:", replyMarkup: null);
        }

        // Метод для скачивания фото и возврата его в виде байтов
        private async Task<byte[]> DownloadPhoto(string fileId)
        {
            var file = await botClient.GetFile(fileId); // Получаем информацию о файле
            using var stream = new MemoryStream();
            await botClient.DownloadFile(file.FilePath, stream); // Скачиваем файл в поток
            return stream.ToArray(); // Возвращаем массив байтов
        }

        // Обработчик нажатий на inline-кнопки
        private async Task HandleCallbackQuery(CallbackQuery callbackQuery)
        {
            var data = callbackQuery.Data;
            if (data.StartsWith("AccessRequest:"))
            {
                var userId = long.Parse(data.Split(':')[1]);
                await botClient.SendMessage(userId, "⌛ Ваш запрос отправлен на рассмотрение");
                await botClient.DeleteMessage(chatId: callbackQuery.From.Id, messageId: callbackQuery.Message.MessageId);
                await botClient.AnswerCallbackQuery(callbackQuery.Id);
                var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new [] { InlineKeyboardButton.WithCallbackData("✅", $"AccessAllowed:{userId}"),InlineKeyboardButton.WithCallbackData("❌", $"AccessDenied:{userId}") },

                });

                var info = await GetUserInfoAsync(callbackQuery.From.Id);
                if (info == null)
                {
                    await botClient.SendMessage(
                        OwnerID,
                        "Не удалось получить информацию о пользователе.",
                        parseMode: ParseMode.MarkdownV2
                    );
                    return;
                }

                // Формируем текст сообщения, исключая пустые поля
                var messageText = new List<string> { "// Запрос доступа //\n" }; // Используем \n для MarkdownV2

                if (!string.IsNullOrEmpty(info.FirstName) || !string.IsNullOrEmpty(info.LastName))
                {
                    var nameParts = new List<string>();
                    if (!string.IsNullOrEmpty(info.FirstName)) nameParts.Add(info.FirstName);
                    if (!string.IsNullOrEmpty(info.LastName)) nameParts.Add(info.LastName);
                    messageText.Add($"👤 {string.Join(" ", nameParts)}");
                }

                if (!string.IsNullOrEmpty(info.Username))
                {
                    messageText.Add($"🐶 @{info.Username}");
                }

                if (!string.IsNullOrEmpty(info.Bio))
                {
                    messageText.Add($"🗨️ {info.Bio}");
                }

                messageText.Add("\nРазрешить доступ?");

                var finalMessageText = string.Join("\n", messageText);

                // Если есть фотография, отправляем фото с подписью
                if (info.Photo?.BigFileId != null)
                {
                    try
                    {
                        // Получаем информацию о файле фотографии
                        var file = await botClient.GetFile(info.Photo.BigFileId);
                        using var stream = new MemoryStream();
                        await botClient.DownloadFile(file.FilePath, stream);
                        stream.Position = 0; // Сбрасываем позицию потока

                        await botClient.SendPhoto(
                            chatId: OwnerID,
                            photo: new InputFileStream(stream),
                            caption: finalMessageText,
                            parseMode: ParseMode.MarkdownV2,
                            replyMarkup: inlineKeyboard
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при отправке фото: {ex.Message}");
                        // В случае ошибки отправляем только текст
                        await botClient.SendMessage(
                            chatId: OwnerID,
                            text: finalMessageText,
                            parseMode: ParseMode.MarkdownV2,
                            replyMarkup: inlineKeyboard
                        );
                    }
                }
                else
                {
                    // Если фото нет, отправляем только текст
                    await botClient.SendMessage(
                        chatId: OwnerID,
                        text: finalMessageText,
                        parseMode: ParseMode.MarkdownV2,
                        replyMarkup: inlineKeyboard
                    );
                }
                await botClient.AnswerCallbackQuery(callbackQuery.Id);
                return;
            }
            else if (data.StartsWith("AccessAllowed:"))
            {
                var userId = long.Parse(data.Split(':')[1]);
                var userGuid = Guid.NewGuid().ToString();
                var userAvatarGuid = Guid.NewGuid().ToString();
                var userAvatarPreviewGuid = Guid.NewGuid().ToString();
                var openApp = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithWebApp("Открыть панель поиска", new WebAppInfo
                        {
                            Url = $"{WEB_APP_URL}/access/{userGuid}"
                        })
                    }
                });
                await botClient.DeleteMessage(
                    chatId: OwnerID,
                    messageId: callbackQuery.Message.MessageId
                );
                await botClient.SendMessage(userId, "Вам разрешили использование этого бота, нажми на кнопку ниже для потверждения", replyMarkup: openApp);
                var userInfo = await GetUserInfoAsync(userId);
                await Program.Minio.UploadBitmapAsync(userAvatarGuid, await GetBitmapFromChatPhotoAsync(userInfo.Photo), MinioService.bucketNames.Avatars);
                await Program.Minio.UploadBitmapAsync(userAvatarPreviewGuid, await GetBitmapFromChatPhotoAsync(userInfo.Photo), MinioService.bucketNames.AvatarsPreview);
                Program.DataBase.AddUser(userId, userInfo.Username, $"{userInfo.FirstName} {userInfo.LastName}", userAvatarGuid, userAvatarPreviewGuid, userGuid);
                await botClient.AnswerCallbackQuery(callbackQuery.Id);
                return;
            }
            else if (data.StartsWith("AccessDenied:"))
            {
                await botClient.DeleteMessage(
                chatId: OwnerID,
                messageId: callbackQuery.Message.MessageId
                );
                await botClient.AnswerCallbackQuery(callbackQuery.Id);
                return;
            }

            if (!Program.DataBase.IsUserAllowedId(callbackQuery.From.Id))
            {
                await botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "У вас нет доступа к этому боту.",
                    showAlert: true
                );
                return;
            }

            if (data.StartsWith("photo_"))
            {
                var itemId = int.Parse(callbackQuery.Data.Split('_')[1]);
                var item = Program.DataBase.GetItemById(itemId);

                if (item != null)
                {
                    // Здесь должен быть код для отправки фото
                    // Например, если у вас есть URL фото в ItemTable или в другом месте
                    await botClient.AnswerCallbackQuery(
                        callbackQuery.Id,
                        $"Запрос фото для товара {item.NameItem} обработан",
                        showAlert: true
                    );

                    // Пример отправки сообщения вместо фото
                    await botClient.SendMessage(
                        callbackQuery.Message.Chat.Id,
                        $"Фото для товара {item.NameItem} (ID: {item.Id})"
                    );
                }
                else
                {
                    await botClient.AnswerCallbackQuery(
                        callbackQuery.Id,
                        "Товар не найден",
                        showAlert: true
                    );
                }
            }
            else if (data.StartsWith("btn_"))
            {
                var buttonId = data.Split('_')[1];
                await botClient.SendMessage(callbackQuery.Message.Chat.Id, $"Вы нажали кнопку {buttonId}");
            }
            else
            {
                await botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "Неизвестная команда",
                    showAlert: true
                );
            }

            await botClient.AnswerCallbackQuery(callbackQuery.Id); // Подтверждаем обработку нажатия
        }
        private async Task HandleInlineQuery(ITelegramBotClient botClient, InlineQuery inlineQuery)
        {
            // Проверяем, есть ли пользователь в базе
            if (!Program.DataBase.IsUserAllowedId(inlineQuery.From.Id))
            {
                var results = new[]
                {
                new InlineQueryResultArticle
                {
                    Id = "no_access",
                    Title = "Доступ запрещен",
                    InputMessageContent = new InputTextMessageContent
                    {
                        MessageText = "У вас нет доступа к этому боту."
                    }
                }
            };

                await botClient.AnswerInlineQuery(inlineQuery.Id, results, cacheTime: 0);
                return;
            }

            // Проверяем, есть ли введенный код
            if (string.IsNullOrWhiteSpace(inlineQuery.Query) || !int.TryParse(inlineQuery.Query, out int code))
            {
                var results = new[]
                {
                new InlineQueryResultArticle
                {
                    Id = "no_code",
                    Title = "Введите код товара",
                    InputMessageContent = new InputTextMessageContent
                    {
                        MessageText = "Пожалуйста, введите код товара."
                    }
                }
            };

                await botClient.AnswerInlineQuery(inlineQuery.Id, results, cacheTime: 0);
                return;
            }

            // Получаем товар из базы
            var item = Program.DataBase.GetItemById(code);
            if (item == null)
            {
                var results = new[]
                {
                new InlineQueryResultArticle
                {
                    Id = "not_found",
                    Title = "Товар не найден",
                    InputMessageContent = new InputTextMessageContent
                    {
                        MessageText = "Товар с указанным кодом не найден в базе данных."
                    }
                }
            };

                await botClient.AnswerInlineQuery(inlineQuery.Id, results, cacheTime: 0);
                return;
            }

            // Формируем ответ с двумя вариантами
            var resultsWithItem = new[]
            {
            new InlineQueryResultArticle
            {
                Id = $"item_{item.Id}_open",
                Title = $"Открыть товар: {item.NameItem}",
                Description = item.FullNameItem,
                InputMessageContent = new InputTextMessageContent
                {
                    MessageText = $"Товар: {item.NameItem}\n" +
                                $"Описание: {item.FullNameItem}\n" +
                                $"Комментарий: {item.Comment}\n" +
                                $"Заметки: {item.Notes}\n" +
                                $"Доверенный: {(item.Trusted ? "Да" : "Нет")}"
                },
                ReplyMarkup = new InlineKeyboardMarkup(
                    InlineKeyboardButton.WithWebApp(
                        "Открыть товар",
                        new WebAppInfo { Url = $"{WEB_APP_URL}/item/{item.Id}" }
                    )
                )
            },
            new InlineQueryResultArticle
            {
                Id = $"item_{item.Id}_photo",
                Title = $"Получить фото: {item.NameItem}",
                Description = item.FullNameItem,
                InputMessageContent = new InputTextMessageContent
                {
                    MessageText = $"Запрос фото для товара: {item.NameItem}"
                },
                ReplyMarkup = new InlineKeyboardMarkup(
                    InlineKeyboardButton.WithCallbackData(
                        "Получить фото",
                        $"photo_{item.Id}"
                    )
                )
            }
        };

            await botClient.AnswerInlineQuery(inlineQuery.Id, resultsWithItem, cacheTime: 0);
        }
        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            ConsoleOutput.LogError($"Ошибка: {exception.Message}");
            return Task.CompletedTask;
        }
        private async Task RemoveReplyKeyboardAsync(long chatId)
        {
            await botClient.SendMessage(
                chatId: chatId,
                text: "",
                replyMarkup: new ReplyKeyboardRemove() // Удаляет reply-кнопки
            );
        }
        public async Task<UserInfo> GetUserInfoAsync(long telegramId)
        {
            try
            {
                // Получаем информацию о чате по telegramId
                var chat = await botClient.GetChat(telegramId);
                // Формируем объект с информацией о пользователе
                return new UserInfo
                {
                    Id = chat.Id,
                    FirstName = chat.FirstName ?? "",
                    LastName = chat.LastName ?? "",
                    Username = chat.Username ?? "",
                    Bio = chat.Bio ?? "",
                    Type = chat.Type.ToString(),
                    Photo = chat.Photo
                };
            }
            catch (Exception ex)
            {
                // Логируем ошибку
                Console.WriteLine($"Ошибка при получении информации о пользователе {telegramId}: {ex.Message}");
                return null; // Или выбросить исключение, в зависимости от ваших требований
            }
        }

        // Метод для преобразования ChatPhoto в Bitmap
        public async Task<Bitmap> GetBitmapFromChatPhotoAsync(ChatPhoto chatPhoto)
        {
            if (chatPhoto == null || string.IsNullOrEmpty(chatPhoto.BigFileId))
            {
                Console.WriteLine("ChatPhoto is null or BigFileId is empty.");
                return null;
            }

            try
            {
                var file = await botClient.GetFile(chatPhoto.BigFileId);
                if (file == null || string.IsNullOrEmpty(file.FilePath))
                {
                    Console.WriteLine("Не удалось получить информацию о файле.");
                    return null;
                }

                // Скачиваем файл в MemoryStream
                using var stream = new MemoryStream();
                await botClient.DownloadFile(file.FilePath, stream);
                stream.Position = 0;

                using var bitmap = new Bitmap(stream);
                return new Bitmap(bitmap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении или преобразовании фотографии: {ex.Message}");
                return null;
            }
        }
    }
}
