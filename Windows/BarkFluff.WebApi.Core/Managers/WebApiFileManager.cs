using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для работы с файлами.
    /// </summary>
    internal class WebApiFileManager : WebApiBase
    {
        private static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromMinutes(5);
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = DefaultHttpTimeout };
        private readonly WebApi _webApi;

        public WebApiFileManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Gets user storage information (used space, limit, and breakdown by file types).
        /// </summary>
        public async Task<(ErrorReturner error, long totalUsedSpace, long totalSpace, Dictionary<Proto.Files.UploadFileType, long> storageByType)> GetUserStorageInfoAsync(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await FilesAC!.GetUserStorageInfoAsync(new Proto.Files.GetUserStorageInfoRequest());
                    // Преобразуем repeated поле в словарь
                    var storageByType = response.StorageByTypes.ToDictionary(
                        x => x.FileType,
                        x => x.UsedStorage
                    );

                    return (new ErrorReturner(true), response.TotalUsedStorage, response.StorageLimit, storageByType);
                }, globalParam);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, $"Ошибка получения информации о хранилище: {ex.Message}"), 0L, 0L, new Dictionary<Proto.Files.UploadFileType, long>());
            }
        }

        /// <summary>
        /// Uploads a file to the server and returns the file ID.
        /// </summary>
        public async Task<(ErrorReturner error, string? fileId)> UploadFileAsync(GlobalParam globalParam, string filePath, Proto.Files.UploadFileType fileType)
        {
            return await UploadFileAsync(globalParam, filePath, fileType, null);
        }

        /// <summary>
        /// Uploads a file to the server with progress reporting and automatic image optimization.
        /// First checks if a file with the same hash already exists to prevent duplicates.
        /// Also checks storage limit before uploading.
        /// </summary>
        public async Task<(ErrorReturner error, string? fileId)> UploadFileAsync(
            GlobalParam globalParam,
            string filePath,
            Proto.Files.UploadFileType fileType,
            IProgress<double>? progress)
        {
            string? processedFilePath = null;
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    progress?.Report(0.05);

                    string fileToUpload = filePath;
                    if (fileType == Proto.Files.UploadFileType.MessageAttachmentImage)
                    {
                        processedFilePath = await ImageProcessor.ProcessImageForUploadAsync(filePath);
                        fileToUpload = processedFilePath;
                        progress?.Report(0.08);
                    }

                    var fileInfo = new FileInfo(fileToUpload);
                    var fileSize = fileInfo.Length;
                    System.Diagnostics.Debug.WriteLine($"WebApi: Uploading file, Size: {fileSize:N0} bytes ({fileSize / 1024.0 / 1024.0:F2} MB), Type: {fileType}");

                    try
                    {
                        var storageInfo = await GetUserStorageInfoAsync(globalParam);
                        if (storageInfo.error.IsSuccess)
                        {
                            var availableSpace = storageInfo.totalSpace - storageInfo.totalUsedSpace;
                            if (fileSize > availableSpace)
                            {
                                return (new ErrorReturner(false, "Недостаточно места в хранилище. Удалите ненужные файлы или свяжитесь с администратором."), null);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        System.Diagnostics.Debug.WriteLine("WebApi: Storage check failed, proceeding with upload");
                    }
                    progress?.Report(0.1);

                    var fileHash = await ComputeFileHashAsync(fileToUpload);
                    progress?.Report(0.15);

                    try
                    {
                        var hashCheckResponse = await FilesAC!.CheckFileHashAsync(new Proto.Files.CheckFileHashRequest
                        {
                            FileHash = fileHash
                        });

                        if (!string.IsNullOrEmpty(hashCheckResponse.FileId))
                        {
                            progress?.Report(1.0);
                            return (new ErrorReturner(true), hashCheckResponse.FileId);
                        }
                    }
                    catch (Exception)
                    {
                        System.Diagnostics.Debug.WriteLine("WebApi: Hash check failed, proceeding with upload");
                    }

                    progress?.Report(0.2);

                    var getLinkUpload = await FilesAC!.GetUploadUrlAsync(new Proto.Files.GetUploadUrlRequest
                    {
                        FileType = fileType
                    });
                    System.Diagnostics.Debug.WriteLine("WebApi: Upload URL obtained");
                    progress?.Report(0.3);

                    await using var fileStream = File.OpenRead(fileToUpload);
                    progress?.Report(0.4);

                    using var formData = new MultipartFormDataContent();
                    using var streamContent = new StreamContent(fileStream);

                    var extension = Path.GetExtension(fileToUpload).ToLowerInvariant();
                    var contentType = extension switch
                    {
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".png" => "image/png",
                        ".gif" => "image/gif",
                        ".webp" => "image/webp",
                        ".mp4" => "video/mp4",
                        ".webm" => "video/webm",
                        ".avi" => "video/x-msvideo",
                        ".mov" => "video/quicktime",
                        ".mkv" => "video/x-matroska",
                        _ => "application/octet-stream"
                    };

                    streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

                    var sanitizedFileName = SanitizeFileName(Path.GetFileName(fileToUpload), extension);
                    System.Diagnostics.Debug.WriteLine("WebApi: Filename sanitized for upload");

                    formData.Add(streamContent, "file", sanitizedFileName);

                    progress?.Report(0.5);

                    var response = await _httpClient.PostAsync(getLinkUpload.Url, formData);
                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"WebApi: Upload failed with status {response.StatusCode}");
                        System.Diagnostics.Debug.WriteLine($"WebApi: Request details - File size: {fileSize} bytes, Content-Type: {contentType}");

                        return (new ErrorReturner(false, $"Ошибка загрузки файла: {response.StatusCode}"), null);
                    }

                    progress?.Report(1.0);

                    return (new ErrorReturner(true), getLinkUpload.FileId);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
            {
                return (new ErrorReturner(false, "Неверный формат идентификатора файла. Он должен быть guid"), null);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, $"Ошибка загрузки файла: {ex.Message}"), null);
            }
            finally
            {
                if (processedFilePath != null && processedFilePath != filePath && File.Exists(processedFilePath))
                {
                    try
                    {
                        File.Delete(processedFilePath);
                    }
                    catch (Exception)
                    {
                        System.Diagnostics.Debug.WriteLine("WebApi: Failed to delete temp file");
                    }
                }
            }
        }

        /// <summary>
        /// Отправляет аватар пользователя на сервер в формате JPEG.
        /// </summary>
        public async Task<ErrorReturner> UploadUserAvatarAsync(GlobalParam globalParam, byte[] jpegImageBytes)
        {
            try
            {
                await _webApi.TokenManager.SafeCallAsync<ErrorReturner>(async () =>
                {
                    var getLinkUpload = await FilesAC!.GetUploadUrlAsync(new Proto.Files.GetUploadUrlRequest
                    {
                        FileType = Proto.Files.UploadFileType.UserAvatar
                    });

                    using var formData = new MultipartFormDataContent();

                    var fileContent = new ByteArrayContent(jpegImageBytes);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

                    formData.Add(fileContent, "file", "avatar.jpg");

                    var response = await _httpClient.PostAsync(getLinkUpload.Url, formData);
                    response.EnsureSuccessStatusCode();

                    try
                    {
                        await UsersAC!.SetProfilePictureAsync(new Proto.Users.SetProfilePictureRequest
                        {
                            FileId = getLinkUpload.FileId
                        });
                    }
                    catch (BarkFluff.Shared.Exceptions.Users.ProfilePictureHasNotValidType)
                    {
                        return new ErrorReturner(false, "Переданный file-id содержит файл не с типом Изображение профиля пользователя");
                    }
                    catch (BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
                    {
                        return new ErrorReturner(false, "Неверный формат идентификатора файла. Он должен быть guid");
                    }

                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
            {
                return new ErrorReturner(false, "Неверный формат идентификатора файла. Он должен быть guid");
            }
            return new ErrorReturner(true);
        }

        public async Task<(ErrorReturner error, string? url)> GetFile(GlobalParam globalParam, string fileId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await FilesAC!.GetTempDownloadUrlAsync(new Proto.Files.GetTempDownloadUrlRequest { FileIds = { fileId } });
                    if (!HasValidFileUrls(response.FileUrls))
                        return (new ErrorReturner(false, "Файл не найден"), string.Empty);
                    return (new ErrorReturner(true), response.FileUrls[0].Url);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
            {
                return (new ErrorReturner(false, "Неверный формат идентификатора файла. Он должен быть guid"), null);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, "Ошибка получения файла"), null);
            }
        }

        public async Task<(ErrorReturner error, List<string>? urls)> GetFiles(GlobalParam globalParam, List<string> fileId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await FilesAC!.GetTempDownloadUrlAsync(new Proto.Files.GetTempDownloadUrlRequest { FileIds = { fileId } });
                    if (!HasValidFileUrls(response.FileUrls))
                        return (new ErrorReturner(false, "Файлы не найдены"), null);
                    var urls = response.FileUrls.Select(f => f.Url).ToList();
                    return (new ErrorReturner(true), urls);
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения файлов"), null);
            }
        }

        /// <summary>
        /// Computes SHA256 hash of a file.
        /// </summary>
        public static async Task<string> ComputeFileHashAsync(string filePath)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            await using var stream = File.OpenRead(filePath);
            var hashBytes = await sha256.ComputeHashAsync(stream);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        /// <summary>
        /// Computes SHA256 hash of a byte array.
        /// </summary>
        public static string ComputeDataHash(byte[] data)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(data);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        /// <summary>
        /// Returns a paginated list of sticker packs.
        /// </summary>
        public async Task<(ErrorReturner error, List<Proto.Files.StickerPackInfo>? packs, int totalCount)> ListStickerPacksAsync(
            GlobalParam globalParam, int offset = 0, int size = 50)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await FilesAC!.ListStickerPacksAsync(new Proto.Files.ListStickerPacksRequest
                    {
                        Pagination = new Proto.Shared.PageRequest { Offset = offset, Size = size }
                    });
                    return (new ErrorReturner(true), response.Packs.ToList(), response.TotalCount);
                }, globalParam);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, $"Ошибка получения стикерпаков: {ex.Message}"), null, 0);
            }
        }

        /// <summary>
        /// Returns a sticker pack with all its stickers.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Files.StickerPackInfo? pack, List<Proto.Files.StickerInfo>? stickers)> GetStickerPackAsync(
            GlobalParam globalParam, string packId)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await FilesAC!.GetStickerPackAsync(new Proto.Files.GetStickerPackRequest
                    {
                        PackId = packId
                    });
                    return (new ErrorReturner(true), response.Pack, response.Stickers.ToList());
                }, globalParam);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, $"Ошибка получения стикерпака: {ex.Message}"), null, null);
            }
        }

        /// <summary>
        /// Checks if a file with the given hash already exists on the server.
        /// </summary>
        public async Task<(ErrorReturner error, string fileId)> CheckFileHashAsync(GlobalParam globalParam, string fileHash)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await FilesAC!.CheckFileHashAsync(new Proto.Files.CheckFileHashRequest
                    {
                        FileHash = fileHash
                    });
                    return (new ErrorReturner(true), response.FileId);
                }, globalParam);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, $"Ошибка проверки хеша файла: {ex.Message}"), string.Empty);
            }
        }

        /// <summary>
        /// Проверяет, что список файловых URL не пуст.
        /// </summary>
        private static bool HasValidFileUrls(Google.Protobuf.Collections.RepeatedField<Proto.Files.GetTempDownloadUrlResponse.Types.DownloadFileData>? fileUrls)
        {
            return fileUrls != null && fileUrls.Count > 0;
        }

        private const int MaxSanitizedFileNameLength = 100;

        /// <summary>
        /// Нормализует имя файла для отправки в form-data: убирает управляющие символы,
        /// заменяет всё кроме [a-zA-Z0-9._-] на '_', обрезает длину. Если имя становится пустым —
        /// генерирует фолбэк на основе расширения.
        /// </summary>
        private static string SanitizeFileName(string fileName, string extension)
        {
            if (string.IsNullOrEmpty(fileName))
                return $"file{extension}";

            var stripped = new System.Text.StringBuilder(fileName.Length);
            foreach (var ch in fileName)
            {
                if (!char.IsControl(ch))
                    stripped.Append(ch);
            }

            var cleaned = System.Text.RegularExpressions.Regex.Replace(stripped.ToString(), @"[^\w\.-]", "_");

            if (cleaned.Length > MaxSanitizedFileNameLength)
            {
                var ext = Path.GetExtension(cleaned);
                var nameOnly = Path.GetFileNameWithoutExtension(cleaned);
                var allowedNameLength = Math.Max(1, MaxSanitizedFileNameLength - ext.Length);
                cleaned = nameOnly.Substring(0, Math.Min(nameOnly.Length, allowedNameLength)) + ext;
            }

            if (string.IsNullOrWhiteSpace(cleaned) || cleaned.All(c => c == '_' || c == '.'))
                return $"file{extension}";

            return cleaned;
        }
    }
}
