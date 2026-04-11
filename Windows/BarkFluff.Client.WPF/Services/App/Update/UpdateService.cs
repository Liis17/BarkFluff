using Newtonsoft.Json;

using System.Net.Http;

namespace BarkFluff.Client.WPF.Services.App.Update
{
    public class UpdateService
    {
        private const string BaseStorageUrl = "https://storage.barkfluff.com/get/barkfluffwindows";
        private readonly HttpClient _httpClient;
        private readonly System.Timers.Timer _timer;
        private readonly string _currentVersion;
        private readonly string _currentType;

        /// <summary>
        /// URL для скачивания найденного обновления (zip-архив)
        /// </summary>
        public string LatestDownloadUrl { get; private set; }

        /// <summary>
        /// Версия найденного обновления
        /// </summary>
        public string LatestVersion { get; private set; }

        public UpdateService(string currentVersion, string currentType)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "BarkFluff-UpdateService");
            _currentVersion = currentVersion;
            _currentType = currentType;
            _timer = new System.Timers.Timer(7200000); // 2 часа
            _timer.Elapsed += async (sender, e) => await CheckForUpdatesAsync();
            _timer.AutoReset = true;
        }

        public async void Start()
        {
            await CheckForUpdatesAsync();
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private string GetChannel()
        {
            return string.Equals(_currentType, "Release", StringComparison.OrdinalIgnoreCase)
                ? "release"
                : "beta";
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                string channel = GetChannel();
                string versionUrl = $"{BaseStorageUrl}/{channel}/version";

                string response = await _httpClient.GetStringAsync(versionUrl);
                var versionInfo = JsonConvert.DeserializeObject<VersionResponse>(response);

                if (versionInfo == null || string.IsNullOrEmpty(versionInfo.Version))
                {
                    OnNoUpdateAvailable();
                    return;
                }

                Version currentVer = new Version(_currentVersion);
                Version remoteVer = new Version(versionInfo.Version);

                if (remoteVer > currentVer)
                {
                    LatestVersion = versionInfo.Version;
                    LatestDownloadUrl = $"{BaseStorageUrl}/{channel}/";

                    WPF.App.ErideMessage.AddMessage($"Доступно обновление: {versionInfo.Version}",
                        new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Info });
                    OnUpdateAvailable(versionInfo.Version, LatestDownloadUrl);
                }
                else
                {
                    OnNoUpdateAvailable();
                }
            }
            catch (Exception ex)
            {
                WPF.App.ErideMessage.AddMessage($"Ошибка проверки обновлений: {ex.Message}",
                    new Erida.MessageType { Type = Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        private class VersionResponse
        {
            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("uploadedAt")]
            public string UploadedAt { get; set; }

            [JsonProperty("fileName")]
            public string FileName { get; set; }
        }

        // Events for update notifications
        public event Action<string, string> UpdateAvailable;
        public event Action NoUpdateAvailable;

        private void OnUpdateAvailable(string version, string downloadUrl) => UpdateAvailable?.Invoke(version, downloadUrl);
        private void OnNoUpdateAvailable() => NoUpdateAvailable?.Invoke();
    }
}