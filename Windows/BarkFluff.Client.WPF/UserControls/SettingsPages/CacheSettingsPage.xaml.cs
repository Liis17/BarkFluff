using BarkFluff.Client.WPF.Services.App.Caching;

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    public partial class CacheSettingsPage : BaseSettingsPage
    {
        public override string Title => "Кеш";

        // Пути из App.xaml.cs
        private const string FileCacheDbPath    = "datas\\file_cache.db";
        private const string MessageCacheDbPath = "datas\\cache.db";
        private const string FileCacheDir       = "datas\\cache";

        public CacheSettingsPage()
        {
            InitializeComponent();
        }

        public override void OnNavigatedTo() => Refresh();

        // ──────────────────────────────────────────────────────────────
        // Подсчёт размеров
        // ──────────────────────────────────────────────────────────────

        private void Refresh()
        {
            StatusText.Text = "Подсчёт...";

            // Размеры по типам файлов из папки cache
            long avatarSz   = DirSize(SubDir("avatars"));
            long imageSz    = DirSize(SubDir("images"));
            long videoSz    = DirSize(SubDir("videos"));
            long gifSz      = DirSize(SubDir("gifs"));
            long docSz      = DirSize(SubDir("documents"));
            long audioSz    = DirSize(SubDir("audio"));
            long fileCacheDb = FileSize(FileCacheDbPath);
            long msgCacheDb  = FileSize(MessageCacheDbPath);

            // Размеры записей в БД сообщений — считаем через LiteDB count (приближённо)
            long msgRecords  = CountDbRecords(MessageCacheDbPath, "messages");
            long chatRecords = CountDbRecords(MessageCacheDbPath, "chats");

            // Суммарный кеш
            long totalFileCache = avatarSz + imageSz + videoSz + gifSz + docSz + audioSz;
            long totalDb        = fileCacheDb + msgCacheDb;
            long totalCache     = totalFileCache + totalDb;

            // Размер самого приложения (exe + dll без datas)
            long appSize = AppBinSize();

            AvatarSize.Text = FormatBytes(avatarSz);
            ImageSize.Text  = FormatBytes(imageSz);
            VideoSize.Text  = FormatBytes(videoSz);
            GifSize.Text    = FormatBytes(gifSz);
            DocSize.Text    = FormatBytes(docSz);
            AudioSize.Text  = FormatBytes(audioSz);
            MsgSize.Text    = $"{FormatBytes(msgCacheDb)} · {msgRecords} записей";
            ChatSize.Text   = $"{FormatBytes(msgCacheDb)} · {chatRecords} чатов";

            TotalUsageText.Text =
                $"Кеш: {FormatBytes(totalCache)}  ·  Приложение: {FormatBytes(appSize)}";

            // Прогресс-бар
            DiskProgress.ClearSegments();
            long grand = appSize + totalCache;
            if (grand > 0)
            {
                DiskProgress.AddSegment(Math.Max(1, (int)(appSize       / 1024)), new SolidColorBrush(Color.FromRgb(0x3D, 0x86, 0xC6)));
                DiskProgress.AddSegment(Math.Max(1, (int)(totalFileCache / 1024)), new SolidColorBrush(Color.FromRgb(0x68, 0xA5, 0x40)));
                DiskProgress.AddSegment(Math.Max(1, (int)(totalDb       / 1024)), new SolidColorBrush(Color.FromRgb(0xCA, 0x6D, 0x34)));
            }
            DiskProgress.AnimStart();

            StatusText.Text = string.Empty;
        }

        // ──────────────────────────────────────────────────────────────
        // Меню «три точки» на каждом блоке
        // ──────────────────────────────────────────────────────────────

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var tag = btn.Tag as string;

            var menu = new ContextMenu();
            var item = new MenuItem { Header = "Очистить" };
            item.Click += (_, _) => ClearByTag(tag);
            menu.Items.Add(item);

            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void ClearByTag(string? tag)
        {
            try
            {
                switch (tag)
                {
                    case "Avatar":
                        App.FileCacheService?.ClearCache(FileType.Avatar);
                        break;
                    case "Image":
                        App.FileCacheService?.ClearCache(FileType.Image);
                        break;
                    case "Video":
                        App.FileCacheService?.ClearCache(FileType.Video);
                        break;
                    case "Gif":
                        App.FileCacheService?.ClearCache(FileType.Gif);
                        break;
                    case "Document":
                        App.FileCacheService?.ClearCache(FileType.Document);
                        break;
                    case "Audio":
                        App.FileCacheService?.ClearCache(FileType.Audio);
                        break;
                    case "Messages":
                        App.CacheManager?.ClearMessages();
                        break;
                    case "Chats":
                        App.CacheManager?.ClearChats();
                        break;
                }

                SetStatus($"Кеш «{TagToName(tag)}» очищен");
            }
            catch (Exception ex)
            {
                SetStatus($"Ошибка: {ex.Message}");
            }

            Refresh();
        }

        // ──────────────────────────────────────────────────────────────
        // Полная очистка
        // ──────────────────────────────────────────────────────────────

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.FileCacheService?.ClearCache();
                App.CacheManager?.ClearAll();
                SetStatus("Весь кеш очищен");
            }
            catch (Exception ex)
            {
                SetStatus($"Ошибка: {ex.Message}");
            }

            Refresh();
        }

        // ──────────────────────────────────────────────────────────────
        // Вспомогательные методы
        // ──────────────────────────────────────────────────────────────

        private static string SubDir(string name)
            => Path.Combine(AppContext.BaseDirectory, FileCacheDir, name);

        private static long DirSize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            try { return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
            catch { return 0; }
        }

        private static long FileSize(string path)
        {
            var full = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
            try { return File.Exists(full) ? new FileInfo(full).Length : 0; }
            catch { return 0; }
        }

        private static long CountDbRecords(string dbPath, string collection)
        {
            var full = Path.IsPathRooted(dbPath) ? dbPath : Path.Combine(AppContext.BaseDirectory, dbPath);
            if (!File.Exists(full)) return 0;
            try
            {
                using var db = new LiteDB.LiteDatabase($"Filename={full};ReadOnly=true;");
                return db.GetCollection(collection).Count();
            }
            catch { return 0; }
        }

        private static long AppBinSize()
        {
            try
            {
                var dir = AppContext.BaseDirectory;
                return Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                                .Where(f => !f.Contains("datas"))
                                .Sum(f =>
                                {
                                    try { return new FileInfo(f).Length; } catch { return 0L; }
                                });
            }
            catch { return 0; }
        }

        private static string FormatBytes(long bytes)
        {
            const double OneMb = 1024.0 * 1024.0;
            const double OneGb = 1024.0 * 1024.0 * 1024.0;
            if (bytes >= OneGb) return (bytes / OneGb).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
            return (bytes / OneMb).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
        }

        private static string TagToName(string? tag) => tag switch
        {
            "Avatar"   => "Аватары",
            "Image"    => "Изображения",
            "Video"    => "Видео",
            "Gif"      => "GIF",
            "Document" => "Документы",
            "Audio"    => "Аудио",
            "Messages" => "Сообщения",
            "Chats"    => "Чаты",
            _          => tag ?? "кеш"
        };

        private void SetStatus(string msg)
            => Dispatcher.Invoke(() => StatusText.Text = msg);
    }
}
