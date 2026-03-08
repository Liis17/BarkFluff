using System.Text.RegularExpressions;

namespace Barkfluff.Updater.CLI.Services
{
    /// <summary>
    /// Информация о релизе
    /// </summary>
    public class ReleaseInfo
    {
        public string TagName { get; set; }
        public string Version { get; set; }
        public string Channel { get; set; } // "Master" или "Dev"
        public int BuildNumber { get; set; }
        public string DownloadUrl { get; set; }
        public string FileName { get; set; }
    }

    /// <summary>
    /// Сервис для работы с GitHub API
    /// </summary>
    public class GitHubReleaseService
    {
        private const string ReleasesApiUrl = "https://api.github.com/repos/Liis17/BarkFluff.Releases/releases";
        private const string UserAgent = "BarkFluff-Updater";

        // Паттерн для парсинга тега: v0.0.0.1064-Dev или v0.0.0.1258-Master
        private static readonly Regex TagPattern = new Regex(@"v(\d+\.\d+\.\d+\.(\d+))-(\w+)", RegexOptions.Compiled);

        public async Task<ReleaseInfo> GetLatestStableReleaseAsync()
        {
            try
            {
                using (var client = CreateHttpClient())
                {
                    var response = await client.GetStringAsync(ReleasesApiUrl);
                    var releases = ParseReleases(response);

                    // Ищем последний Master или Release (не Dev)
                    ReleaseInfo latestStable = null;
                    foreach (var release in releases)
                    {
                        if (release.Channel.Equals("Master", StringComparison.OrdinalIgnoreCase) ||
                            release.Channel.Equals("Release", StringComparison.OrdinalIgnoreCase))
                        {
                            if (latestStable == null || release.BuildNumber > latestStable.BuildNumber)
                            {
                                latestStable = release;
                            }
                        }
                    }

                    return latestStable;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error fetching releases: {ex.Message}", ex);
            }
        }

        private HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler();
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            return client;
        }

        private List<ReleaseInfo> ParseReleases(string json)
        {
            var releases = new List<ReleaseInfo>();

            // Простой парсинг JSON без внешних библиотек
            // Ищем все tag_name и browser_download_url
            var tagMatches = Regex.Matches(json, @"""tag_name""\s*:\s*""([^""]+)""");
            var assetBlocks = Regex.Matches(json, @"""assets""\s*:\s*\[(.*?)\]", RegexOptions.Singleline);

            for (int i = 0; i < tagMatches.Count; i++)
            {
                var tagName = tagMatches[i].Groups[1].Value;
                var tagMatch = TagPattern.Match(tagName);

                if (tagMatch.Success)
                {
                    var release = new ReleaseInfo
                    {
                        TagName = tagName,
                        Version = tagMatch.Groups[1].Value,
                        BuildNumber = int.Parse(tagMatch.Groups[2].Value),
                        Channel = tagMatch.Groups[3].Value
                    };

                    // Ищем URL для скачивания wpf-build.zip
                    if (i < assetBlocks.Count)
                    {
                        var assetBlock = assetBlocks[i].Groups[1].Value;
                        var urlMatch = Regex.Match(assetBlock, @"""browser_download_url""\s*:\s*""([^""]*wpf-build\.zip[^""]*)""");
                        if (urlMatch.Success)
                        {
                            release.DownloadUrl = urlMatch.Groups[1].Value;
                            release.FileName = "wpf-build.zip";
                            releases.Add(release);
                        }
                    }
                }
            }

            return releases;
        }
    }
}
