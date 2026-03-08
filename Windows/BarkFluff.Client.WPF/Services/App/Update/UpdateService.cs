using Newtonsoft.Json;

using System.IO;
using System.Net.Http;

namespace BarkFluff.Client.WPF.Services.App.Update
{
    public class UpdateService
    {
        private readonly string _repoOwner = "Liis17";
        private readonly string _repoName = "BarkFluff.Releases";
        private readonly HttpClient _httpClient;
        private System.Timers.Timer _timer;
        private string _currentVersion;
        private string _currentType;
        private const string UpdaterFileName = "Barkfluff.Updater.CLI.exe";
        private const string UpdaterDownloadUrl = "https://barkfluff.com/download/installer";

        public UpdateService(string currentVersion, string currentType)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "UpdateService");
            _currentVersion = currentVersion;
            _currentType = currentType;
            _timer = new System.Timers.Timer(7200000); // 2 hours = 7200000 ms
            _timer.Elapsed += async (sender, e) => await CheckForUpdatesAsync();
            _timer.AutoReset = true;
        }

        public async void Start()
        {
            // Check for updater on startup
            await EnsureUpdaterExistsAsync();

            // Check for updates immediately on startup
            await CheckForUpdatesAsync();

            // Start timer for periodic checks
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private async Task EnsureUpdaterExistsAsync()
        {
            try
            {
                string appDirectory = AppContext.BaseDirectory;
                string updaterPath = Path.Combine(appDirectory, UpdaterFileName);

                if (!File.Exists(updaterPath))
                {
                    WPF.App.ErideMessage.AddMessage($"Загрузка средства обновления...", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Info });

                    byte[] updaterBytes = await _httpClient.GetByteArrayAsync(UpdaterDownloadUrl);
                    await File.WriteAllBytesAsync(updaterPath, updaterBytes);

                    WPF.App.ErideMessage.AddMessage($"Средство обновления успешно загружено", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Info });
                }
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage.AddMessage($"Ошибка загрузки {UpdaterFileName}: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                string apiUrl = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases";
                string response = await _httpClient.GetStringAsync(apiUrl);
                var releases = JsonConvert.DeserializeObject<List<Release>>(response);
                Release latestSuitable = null;
                Version currentVer = new Version(_currentVersion);

                foreach (var release in releases)
                {
                    if (ParseRelease(release.TagName, out string relType, out string relVersion))
                    {
                        // If current type is Release, skip Dev releases
                        if (_currentType == "Release" && relType == "Dev") continue;

                        Version relVer = new Version(relVersion);
                        if (relVer > currentVer && (latestSuitable == null || relVer > new Version(latestSuitable.Version)))
                        {
                            latestSuitable = new Release { TagName = release.TagName, Version = relVersion, Type = relType, Assets = release.Assets };
                        }
                    }
                }

                if (latestSuitable != null)
                {
                    WPF.App.ErideMessage.AddMessage($"Доступно обновление: {latestSuitable.TagName}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Info });
                    OnUpdateAvailable(latestSuitable.TagName, latestSuitable.Version);
                }
                else
                {
                    OnNoUpdateAvailable();
                }
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage.AddMessage($"Ошибка проверки обновлений: {ex.Message}", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        private bool ParseRelease(string tag, out string type, out string version)
        {
            type = null;
            version = null;
            if (tag.StartsWith("v"))
            {
                string withoutV = tag.Substring(1);
                var parts = withoutV.Split('-');
                if (parts.Length == 2)
                {
                    version = parts[0];
                    type = parts[1];
                    // Нормализуем тип
                    if (string.Equals(type, "dev", StringComparison.OrdinalIgnoreCase)) type = "Dev";
                    else if (string.Equals(type, "release", StringComparison.OrdinalIgnoreCase)) type = "Release";
                    return !string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(type);
                }
            }
            return false;
        }

        private class Release
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set; }
            public string Version { get; set; }
            public string Type { get; set; }
            [JsonProperty("assets")]
            public List<Asset> Assets { get; set; }
        }

        private class Asset
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
            [JsonProperty("digest")]
            public string Digest { get; set; }
        }

        // Events for update notifications
        public event Action<string, string> UpdateAvailable;
        public event Action NoUpdateAvailable;

        private void OnUpdateAvailable(string tagName, string version) => UpdateAvailable?.Invoke(tagName, version);
        private void OnNoUpdateAvailable() => NoUpdateAvailable?.Invoke();
    }
}