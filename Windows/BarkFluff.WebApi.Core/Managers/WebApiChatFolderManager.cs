using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер папок чатов. Папки хранятся в сервисе Users, но оперируют
    /// идентификаторами чатов из сервиса Messages (Guid-строки, как Chat.Id).
    /// </summary>
    internal class WebApiChatFolderManager : WebApiBase
    {
        private readonly WebApi _webApi;

        public WebApiChatFolderManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Папки текущего пользователя, отсортированные сервером по SortOrder.
        /// </summary>
        public async Task<(ErrorReturner error, List<Proto.Users.ChatFolderData>? folders)> GetChatFolders(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.GetChatFoldersAsync(new Proto.Users.GetChatFoldersRequest());
                    return (new ErrorReturner(true), response.Folders.ToList());
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                return (new ErrorReturner(false, "Пользователь не подтверждён"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения папок чатов"), null);
            }
        }

        /// <summary>
        /// Создать папку. Иконка опциональна.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Users.ChatFolderData? folder)> CreateChatFolder(
            string folderName, GlobalParam globalParam, string folderIcon = "")
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.CreateChatFolderAsync(new Proto.Users.CreateChatFolderRequest
                    {
                        FolderName = folderName,
                        FolderIcon = folderIcon
                    });
                    return ((ErrorReturner, Proto.Users.ChatFolderData?))(new ErrorReturner(true), response.Folder);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.ChatFolderInvalidNameException)
            {
                return (new ErrorReturner(false, "Недопустимое название папки"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка создания папки"), null);
            }
        }

        /// <summary>
        /// Изменить папку. Название и иконка меняются только если переданы (null = не трогать),
        /// список чатов заменяется целиком только если передан не-null.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Users.ChatFolderData? folder)> UpdateChatFolder(
            string folderId,
            GlobalParam globalParam,
            string? folderName = null,
            string? folderIcon = null,
            List<string>? chatList = null)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new Proto.Users.UpdateChatFolderRequest
                    {
                        FolderId = folderId,
                        HasChatListUpdate = chatList != null
                    };
                    if (folderName != null)
                        request.FolderName = folderName;
                    if (folderIcon != null)
                        request.FolderIcon = folderIcon;
                    if (chatList != null)
                        request.ChatList.AddRange(chatList);

                    var response = await UsersAC!.UpdateChatFolderAsync(request);
                    return ((ErrorReturner, Proto.Users.ChatFolderData?))(new ErrorReturner(true), response.Folder);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.ChatFolderNotFoundException)
            {
                return (new ErrorReturner(false, "Папка не найдена"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Users.ChatFolderInvalidNameException)
            {
                return (new ErrorReturner(false, "Недопустимое название папки"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка изменения папки"), null);
            }
        }

        /// <summary>
        /// Удалить папку. Чаты в ней не удаляются.
        /// </summary>
        public async Task<ErrorReturner> DeleteChatFolder(string folderId, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await UsersAC!.DeleteChatFolderAsync(new Proto.Users.DeleteChatFolderRequest { FolderId = folderId });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.ChatFolderNotFoundException)
            {
                return new ErrorReturner(false, "Папка не найдена");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка удаления папки");
            }
        }

        /// <summary>
        /// Добавить чат в папку.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Users.ChatFolderData? folder)> AddChatToFolder(
            string folderId, string chatId, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.AddChatToFolderAsync(new Proto.Users.AddChatToFolderRequest
                    {
                        FolderId = folderId,
                        ChatId = chatId
                    });
                    return ((ErrorReturner, Proto.Users.ChatFolderData?))(new ErrorReturner(true), response.Folder);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.ChatFolderNotFoundException)
            {
                return (new ErrorReturner(false, "Папка не найдена"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка добавления чата в папку"), null);
            }
        }

        /// <summary>
        /// Убрать чат из папки.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Users.ChatFolderData? folder)> RemoveChatFromFolder(
            string folderId, string chatId, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.RemoveChatFromFolderAsync(new Proto.Users.RemoveChatFromFolderRequest
                    {
                        FolderId = folderId,
                        ChatId = chatId
                    });
                    return ((ErrorReturner, Proto.Users.ChatFolderData?))(new ErrorReturner(true), response.Folder);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.ChatFolderNotFoundException)
            {
                return (new ErrorReturner(false, "Папка не найдена"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка удаления чата из папки"), null);
            }
        }

        /// <summary>
        /// Изменить порядок папок. Ключ словаря — folderId, значение — новый SortOrder.
        /// </summary>
        public async Task<ErrorReturner> ReorderChatFolders(Dictionary<string, int> orders, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new Proto.Users.ReorderChatFoldersRequest();
                    request.Orders.AddRange(orders.Select(o => new Proto.Users.ChatFolderOrder
                    {
                        FolderId = o.Key,
                        SortOrder = o.Value
                    }));

                    await UsersAC!.ReorderChatFoldersAsync(request);
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.ChatFolderNotFoundException)
            {
                return new ErrorReturner(false, "Папка не найдена");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка изменения порядка папок");
            }
        }
    }
}
