using LiteDB;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.Services.App.Caching
{
    /// <summary>
    /// Сервис кеширования файлов
    /// </summary>
    public class FileCacheService : IDisposable
    {
        private readonly string _baseCacheDir;
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<CachedFile> _files;
        private readonly object _lock = new object();
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(5); // Ограничение на 5 параллельных загрузок
        private bool _disposed = false;

        // Placeholders для разных типов файлов
        /// <summary>
        /// Placeholder по умолчанию для всех типов файлов
        /// </summary>
        public const string DefaultPlaceholder = "pack://application:,,,/BarkFluff;component/Resources/Placeholders/userplaceholder.png";

        /// <summary>
        /// Placeholder для изображений
        /// </summary>
        public const string ImagePlaceholder = "pack://application:,,,/BarkFluff;component/Resources/Placeholders/image.png";

        /// <summary>
        /// Placeholder для видео
        /// </summary>
        public const string VideoPlaceholder = "pack://application:,,,/BarkFluff;component/Resources/Placeholders/video.png";

        /// <summary>
        /// Placeholder для GIF
        /// </summary>
        public const string GifPlaceholder = "pack://application:,,,/BarkFluff;component/Resources/Placeholders/gif.png";

        /// <summary>
        /// Placeholder для документов
        /// </summary>
        public const string DocumentPlaceholder = "pack://application:,,,/BarkFluff;component/Resources/Placeholders/document.png";

        /// <summary>
        /// Placeholder для аудио
        /// </summary>
        public const string AudioPlaceholder = "pack://application:,,,/BarkFluff;component/Resources/Placeholders/audio.png";

        /// <summary>
        /// Событие, вызываемое при успешном кешировании файла
        /// </summary>
        public event Action<string, string, FileType>? FileCached;

        /// <summary>
        /// Событие, вызываемое при ошибке загрузки файла
        /// </summary>
        public event Action<string, string>? FileDownloadFailed;

        /// <summary>
        /// Создает новый экземпляр сервиса кеширования файлов
        /// </summary>
        /// <param name="dbPath">Путь к базе данных LiteDB</param>
        /// <param name="baseCacheDir">Базовая директория для кеша</param>
        public FileCacheService(string dbPath, string baseCacheDir)
        {
            _baseCacheDir = baseCacheDir;

            // Создаем директории для разных типов файлов
            Directory.CreateDirectory(GetCacheDirectory(FileType.Avatar));
            Directory.CreateDirectory(GetCacheDirectory(FileType.Image));
            Directory.CreateDirectory(GetCacheDirectory(FileType.Video));
            Directory.CreateDirectory(GetCacheDirectory(FileType.Gif));
            Directory.CreateDirectory(GetCacheDirectory(FileType.Document));
            Directory.CreateDirectory(GetCacheDirectory(FileType.Audio));

            _db = new LiteDatabase(dbPath);
            _files = _db.GetCollection<CachedFile>("cached_files");
            _files.EnsureIndex(x => x.Hash);
            _files.EnsureIndex(x => x.FileType);
        }

        /// <summary>
        /// Получает директорию кеша для указанного типа файла
        /// </summary>
        private string GetCacheDirectory(FileType fileType)
        {
            string subDir = fileType switch
            {
                FileType.Avatar => "avatars",
                FileType.Image => "images",
                FileType.Video => "videos",
                FileType.Gif => "gifs",
                FileType.Document => "documents",
                FileType.Audio => "audio",
                _ => "other"
            };
            return Path.Combine(_baseCacheDir, subDir);
        }

        /// <summary>
        /// Получает placeholder для указанного типа файла
        /// </summary>
        private static string GetPlaceholder(FileType fileType)
        {
            return fileType switch
            {
                FileType.Avatar => DefaultPlaceholder,
                FileType.Image => ImagePlaceholder,
                FileType.Video => VideoPlaceholder,
                FileType.Gif => GifPlaceholder,
                FileType.Document => DocumentPlaceholder,
                FileType.Audio => AudioPlaceholder,
                _ => DefaultPlaceholder
            };
        }

        /// <summary>
        /// Возвращает placeholder для указанного типа файла (публичный метод)
        /// </summary>
        public static string GetPlaceholderForType(FileType fileType) => GetPlaceholder(fileType);

        /// <summary>
        /// Получает путь к закешированному файлу или placeholder, если файл не найден.
        /// Асинхронно скачивает файл, если он отсутствует.
        /// </summary>
        /// <param name="fileId">Идентификатор файла</param>
        /// <param name="fileType">Тип файла</param>
        /// <param name="providedUrl">Опциональный URL для прямой загрузки</param>
        /// <returns>Путь к файлу или placeholder</returns>
        public string GetCachedFilePath(string fileId, FileType fileType, string? providedUrl = null)
        {
            if (string.IsNullOrEmpty(fileId))
            {
                return GetPlaceholder(fileType);
            }

            lock (_lock)
            {
                var cached = _files.FindOne(x => x.Hash == fileId);
                if (cached != null && File.Exists(cached.Path))
                {
                    return cached.Path;
                }
            }

            // Запускаем асинхронную загрузку
            _ = Task.Run(() => DownloadAndCacheFileAsync(fileId, fileType, providedUrl));

            return GetPlaceholder(fileType);
        }

        /// <summary>
        /// Асинхронно получает путь к закешированному файлу.
        /// Ожидает завершения загрузки, если файл отсутствует.
        /// </summary>
        /// <param name="fileId">Идентификатор файла</param>
        /// <param name="fileType">Тип файла</param>
        /// <param name="providedUrl">Опциональный URL для прямой загрузки</param>
        /// <returns>Путь к файлу</returns>
        public async Task<string> GetCachedFilePathAsync(string fileId, FileType fileType, string? providedUrl = null)
        {
            if (string.IsNullOrEmpty(fileId))
            {
                return GetPlaceholder(fileType);
            }

            lock (_lock)
            {
                var cached = _files.FindOne(x => x.Hash == fileId);
                if (cached != null && File.Exists(cached.Path))
                {
                    return cached.Path;
                }
            }

            var result = await DownloadAndCacheFileAsync(fileId, fileType, providedUrl);
            return result ?? GetPlaceholder(fileType);
        }

        /// <summary>
        /// Асинхронно получает изображение из кеша или загружает его.
        /// </summary>
        /// <param name="fileId">Идентификатор файла</param>
        /// <param name="fileType">Тип файла (по умолчанию Avatar)</param>
        /// <param name="providedUrl">Опциональный URL для прямой загрузки</param>
        /// <returns>ImageSource для использования в UI</returns>
        public async Task<ImageSource> GetCachedImageAsync(string fileId, FileType fileType = FileType.Avatar, string? providedUrl = null)
        {
            var path = await GetCachedFilePathAsync(fileId, fileType, providedUrl);
            return CreateImageSource(path);
        }

        /// <summary>
        /// Проверяет, является ли URL placeholder'ом (pack:// схема)
        /// </summary>
        /// <param name="url">URL для проверки</param>
        /// <returns>True, если URL является placeholder'ом</returns>
        public static bool IsPlaceholder(string? url)
        {
            return !string.IsNullOrEmpty(url) && url.StartsWith("pack://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Извлекает fileId из URL если возможно
        /// </summary>
        /// <param name="url">URL для извлечения fileId</param>
        /// <returns>FileId или null, если не удалось извлечь</returns>
        public static string? ExtractFileIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || IsPlaceholder(url)) return null;

            try
            {
                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Split('/');
                if (segments.Length > 0)
                {
                    var lastSegment = segments[^1];
                    // Удаляем расширение если есть
                    var dotIndex = lastSegment.LastIndexOf('.');
                    if (dotIndex > 0)
                    {
                        lastSegment = lastSegment.Substring(0, dotIndex);
                    }
                    // Проверяем, является ли это GUID
                    if (Guid.TryParse(lastSegment, out _))
                    {
                        return lastSegment;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Создает ImageSource из пути к файлу
        /// </summary>
        private ImageSource CreateImageSource(string path)
        {
            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Делаем потокобезопасным
                return bitmapImage;
            }
            catch
            {
                // Возвращаем placeholder при ошибке
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(DefaultPlaceholder, UriKind.RelativeOrAbsolute);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
        }

        /// <summary>
        /// Загружает и кеширует файл
        /// </summary>
        private async Task<string?> DownloadAndCacheFileAsync(string fileId, FileType fileType, string? providedUrl, string? originalFileName = null)
        {
            Debug.WriteLine($"[FileCacheService] DownloadAndCacheFileAsync started: fileId={fileId}, fileType={fileType}, providedUrl={providedUrl}");
            await _downloadSemaphore.WaitAsync();
            try
            {
                // Проверяем еще раз, возможно файл уже был загружен
                lock (_lock)
                {
                    var cached = _files.FindOne(x => x.Hash == fileId);
                    if (cached != null && File.Exists(cached.Path))
                    {
                        Debug.WriteLine($"[FileCacheService] File already cached at: {cached.Path}");
                        return cached.Path;
                    }
                }

                string url;
                if (!string.IsNullOrEmpty(providedUrl))
                {
                    url = providedUrl;
                    Debug.WriteLine($"[FileCacheService] Using provided URL: {url}");
                }
                else
                {
                    Debug.WriteLine($"[FileCacheService] No URL provided, calling GetFile API for fileId={fileId}");
                    // Получаем URL через API
                    var response = await WPF.App.ServerCommunication.GetFile(WPF.App.GParam, fileId);
                    if (!response.error.IsSuccess || string.IsNullOrEmpty(response.url))
                    {
                        var errorMessage = response.error.ErrorMessage ?? "Неизвестная ошибка";
                        Debug.WriteLine($"[FileCacheService] GetFile API FAILED: {errorMessage}");
                        WPF.App.ErideMessage?.AddMessage(
                            $"Не удалось загрузить файл {fileId}: {errorMessage}",
                            new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                        FileDownloadFailed?.Invoke(fileId, errorMessage);
                        return null;
                    }
                    url = response.url;
                    Debug.WriteLine($"[FileCacheService] GetFile API returned URL: {url}");
                }

                // Определяем расширение файла
                var extension = ResolveFileExtension(url, fileType, originalFileName);

                var cacheDir = GetCacheDirectory(fileType);

                Debug.WriteLine($"[FileCacheService] Downloading from: {url}");
                // Загружаем файл
                var bytes = await _httpClient.GetByteArrayAsync(url);

                // Конвертируем WebP в PNG для совместимости с WPF (WPF не поддерживает WebP нативно)
                if (IsWebPContent(bytes))
                {
                    bytes = ConvertWebPToPng(bytes);
                    extension = ".png";
                }

                var filePath = Path.Combine(cacheDir, $"{fileId}{extension}");
                await File.WriteAllBytesAsync(filePath, bytes);
                Debug.WriteLine($"[FileCacheService] Downloaded {bytes.Length} bytes to: {filePath}");

                // Сохраняем в базу данных используя Upsert
                lock (_lock)
                {
                    _files.Upsert(new CachedFile
                    {
                        Hash = fileId,
                        Path = filePath,
                        FileType = fileType
                    });
                }

                // Вызываем событие
                Debug.WriteLine($"[FileCacheService] Invoking FileCached event for fileId={fileId}, path={filePath}");
                FileCached?.Invoke(fileId, filePath, fileType);

                return filePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileCacheService] Download EXCEPTION for fileId={fileId}: {ex.Message}");
                WPF.App.ErideMessage?.AddMessage(
                    $"Ошибка при загрузке файла {fileId}: {ex.Message}",
                    new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                FileDownloadFailed?.Invoke(fileId, ex.Message);
                return null;
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }

        /// <summary>
        /// Загружает и кеширует файл с отчётом о прогрессе
        /// </summary>
        public async Task<string?> DownloadAndCacheFileWithProgressAsync(string fileId, FileType fileType, string? providedUrl, IProgress<double> progress, string? originalFileName = null)
        {
            await _downloadSemaphore.WaitAsync();
            try
            {
                lock (_lock)
                {
                    var cached = _files.FindOne(x => x.Hash == fileId);
                    if (cached != null && File.Exists(cached.Path))
                    {
                        progress.Report(1.0);
                        return cached.Path;
                    }
                }

                string url;
                if (!string.IsNullOrEmpty(providedUrl))
                {
                    url = providedUrl;
                }
                else
                {
                    var response = await WPF.App.ServerCommunication.GetFile(WPF.App.GParam, fileId);
                    if (!response.error.IsSuccess || string.IsNullOrEmpty(response.url))
                    {
                        FileDownloadFailed?.Invoke(fileId, response.error.ErrorMessage ?? "Неизвестная ошибка");
                        return null;
                    }
                    url = response.url;
                }

                var extension = ResolveFileExtension(url, fileType, originalFileName);

                var cacheDir = GetCacheDirectory(fileType);

                using var response2 = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response2.EnsureSuccessStatusCode();
                var totalBytes = response2.Content.Headers.ContentLength ?? -1;

                await using var contentStream = await response2.Content.ReadAsStreamAsync();
                using var memoryStream = new MemoryStream();

                var buffer = new byte[8192];
                long downloadedBytes = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloadedBytes += bytesRead;
                    if (totalBytes > 0)
                    {
                        progress.Report((double)downloadedBytes / totalBytes);
                    }
                }

                progress.Report(1.0);

                var bytes = memoryStream.ToArray();

                // Конвертируем WebP в PNG для совместимости с WPF
                if (IsWebPContent(bytes))
                {
                    bytes = ConvertWebPToPng(bytes);
                    extension = ".png";
                }

                var filePath = Path.Combine(cacheDir, $"{fileId}{extension}");
                await File.WriteAllBytesAsync(filePath, bytes);

                lock (_lock)
                {
                    _files.Upsert(new CachedFile
                    {
                        Hash = fileId,
                        Path = filePath,
                        FileType = fileType
                    });
                }

                FileCached?.Invoke(fileId, filePath, fileType);
                return filePath;
            }
            catch (Exception ex)
            {
                FileDownloadFailed?.Invoke(fileId, ex.Message);
                return null;
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }

        /// <summary>
        /// Определяет расширение файла: приоритет — оригинальное имя, затем URL, затем fallback по типу
        /// </summary>
        private static string ResolveFileExtension(string url, FileType fileType, string? originalFileName)
        {
            // 1. Из оригинального имени файла
            if (!string.IsNullOrEmpty(originalFileName))
            {
                var ext = Path.GetExtension(originalFileName);
                if (!string.IsNullOrEmpty(ext))
                    return ext;
            }

            // 2. Из URL
            try
            {
                var ext = Path.GetExtension(new Uri(url).AbsolutePath);
                if (!string.IsNullOrEmpty(ext))
                    return ext;
            }
            catch { }

            // 3. Fallback по типу файла
            return fileType switch
            {
                FileType.Avatar => ".png",
                FileType.Image => ".png",
                FileType.Video => ".mp4",
                FileType.Gif => ".gif",
                FileType.Document => ".bin",
                FileType.Audio => ".mp3",
                _ => ".bin"
            };
        }

        /// <summary>
        /// Проверяет, является ли содержимое файла форматом WebP (по magic bytes: RIFF....WEBP)
        /// </summary>
        private static bool IsWebPContent(byte[] data)
        {
            return data.Length > 12
                && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 // RIFF
                && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50; // WEBP
        }

        /// <summary>
        /// Конвертирует WebP-изображение в PNG с сохранением прозрачности
        /// </summary>
        private static byte[] ConvertWebPToPng(byte[] webpData)
        {
            using var image = SixLabors.ImageSharp.Image.Load(webpData);
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Проверяет, есть ли файл в кеше
        /// </summary>
        /// <param name="fileId">Идентификатор файла</param>
        /// <returns>True, если файл закеширован</returns>
        public bool IsFileCached(string fileId)
        {
            if (string.IsNullOrEmpty(fileId)) return false;

            lock (_lock)
            {
                var cached = _files.FindOne(x => x.Hash == fileId);
                return cached != null && File.Exists(cached.Path);
            }
        }

        /// <summary>
        /// Очищает кеш указанного типа файлов
        /// </summary>
        public void ClearCache(FileType? fileType = null)
        {
            lock (_lock)
            {
                if (fileType.HasValue)
                {
                    var files = _files.Find(x => x.FileType == fileType.Value);
                    foreach (var file in files)
                    {
                        try
                        {
                            if (File.Exists(file.Path))
                            {
                                File.Delete(file.Path);
                            }
                        }
                        catch { }
                    }
                    _files.DeleteMany(x => x.FileType == fileType.Value);
                }
                else
                {
                    var files = _files.FindAll();
                    foreach (var file in files)
                    {
                        try
                        {
                            if (File.Exists(file.Path))
                            {
                                File.Delete(file.Path);
                            }
                        }
                        catch { }
                    }
                    _files.DeleteAll();
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _db.Dispose();
            _httpClient.Dispose();
            _downloadSemaphore.Dispose();
        }
    }
}
