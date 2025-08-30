using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.IO;
using System.Threading.Tasks;

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
        private const string UpdatesFolder = "updates";
        private const string HashFileName = "release.hash";
        private const string ZipFileName = "wpf-build.zip";

        public UpdateService(string currentVersion, string currentType)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "UpdateService");
            _currentVersion = currentVersion;
            _currentType = currentType;
            _timer = new System.Timers.Timer(300000); // 5 минут = 300000 мс
            _timer.Elapsed += async (sender, e) => await CheckForUpdatesAsync();
            _timer.AutoReset = true;
        }

        public void Start()
        {
            Task.Run(() => _timer.Start());
        }

        public void Stop()
        {
            _timer.Stop();
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
                    string zipDownloadUrl = latestSuitable.Assets?.Find(a => a.Name == ZipFileName)?.BrowserDownloadUrl;
                    string hash = latestSuitable.Assets?.Find(a => a.Name == ZipFileName)?.Digest;

                    if (string.IsNullOrEmpty(zipDownloadUrl) || string.IsNullOrEmpty(hash))
                    {
                        WPF.App.ErideMessage.AddMessage("Не найдены файлы обновления в релизе.", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
                        return;
                    }

                    Directory.CreateDirectory(UpdatesFolder);
                    string hashPath = Path.Combine(UpdatesFolder, HashFileName);
                    string zipPath = Path.Combine(UpdatesFolder, ZipFileName);

                    hash = hash.Trim();

                    bool needDownload = true;
                    if (File.Exists(hashPath))
                    {
                        string localHash = File.ReadAllText(hashPath).Trim();
                        if (localHash == hash && File.Exists(zipPath))
                        {
                            needDownload = false;
                        }
                    }

                    if (needDownload)
                    {
                        // Скачивание в отдельном потоке
                        await Task.Run(async () =>
                        {
                            byte[] zipBytes = await _httpClient.GetByteArrayAsync(zipDownloadUrl);
                            File.WriteAllBytes(zipPath, zipBytes);
                            File.WriteAllText(hashPath, hash);
                        });
                    }

                    // Уведомление о готовом обновлении
                    WPF.App.ErideMessage.AddMessage($"Обновление доступно: {latestSuitable.TagName}. Файл готов в {zipPath}.", new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Info });
                    OnUpdateAvailable(zipPath);
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

        // Пример события для уведомления об обновлении
        public event Action<string> UpdateAvailable;
        private void OnUpdateAvailable(string path) => UpdateAvailable?.Invoke(path);
    }
}