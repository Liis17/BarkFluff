#include "GitHubService.h"

GitHubService::GitHubService(HttpClient* client)
    : m_httpClient(client)
{
}

ReleaseInfo GitHubService::GetLatestStableRelease()
{
    m_lastError.clear();

    std::wstring json = m_httpClient->FetchJson(App::GITHUB_HOST, App::GITHUB_RELEASES_URL);
    if (json.empty())
    {
        m_lastError = L"Failed to fetch releases: " + m_httpClient->GetLastError();
        return ReleaseInfo();
    }

    auto releases = ParseReleases(json);

    ReleaseInfo latestStable;
    for (const auto& release : releases)
    {
        if (_wcsicmp(release.channel.c_str(), L"Master") == 0 ||
            _wcsicmp(release.channel.c_str(), L"Release") == 0)
        {
            if (!latestStable.buildNumber || release.buildNumber > latestStable.buildNumber)
            {
                latestStable = release;
            }
        }
    }

    if (!latestStable.buildNumber)
    {
        m_lastError = L"No stable release found (Master/Release)";
    }

    return latestStable;
}

std::vector<ReleaseInfo> GitHubService::ParseReleases(const std::wstring& json)
{
    std::vector<ReleaseInfo> releases;

    // Simple regex-based JSON parsing (similar to C# version)
    // Pattern: v0.0.0.1064-Dev or v0.0.0.1258-Master
    static std::wregex tagPattern(LR"("tag_name"\s*:\s*"v(\d+\.\d+\.\d+\.(\d+))-(\w+))");

    // Find all tag_name matches
    std::wstring::const_iterator searchStart(json.cbegin());
    std::vector<std::pair<std::wstring, size_t>> tagMatches;

    std::wsmatch match;
    while (std::regex_search(searchStart, json.cend(), match, tagPattern))
    {
        tagMatches.push_back({ match[0].str(), match.position() });
        searchStart = match.suffix().first;
    }

    // Find asset blocks and download URLs
    static std::wregex assetsBlockPattern(LR"("assets"\s*:\s*\[(.*?)\])", std::regex::ECMAScript | std::regex::dotall);
    static std::wregex downloadUrlPattern(LR"("browser_download_url"\s*:\s*"([^"]*wpf-build\.zip[^"]*)")");

    std::vector<std::wstring> assetBlocks;
    searchStart = json.cbegin();
    while (std::regex_search(searchStart, json.cend(), match, assetsBlockPattern))
    {
        assetBlocks.push_back(match[1].str());
        searchStart = match.suffix().first;
    }

    // Match tags with assets
    for (size_t i = 0; i < tagMatches.size() && i < assetBlocks.size(); i++)
    {
        auto& tagMatch = tagMatches[i];
        std::wstring tagStr = tagMatch.first;

        // Parse tag: v0.0.0.1064-Dev
        std::wsmatch tagParts;
        if (std::regex_search(tagStr, tagParts, tagPattern))
        {
            ReleaseInfo release;
            release.tagName = L"v" + tagParts[1].str();
            release.version = tagParts[1].str();
            release.buildNumber = _wtoi(tagParts[2].str().c_str());
            release.channel = tagParts[3].str();

            // Find download URL in asset block
            std::wsmatch urlMatch;
            if (std::regex_search(assetBlocks[i], urlMatch, downloadUrlPattern))
            {
                release.downloadUrl = urlMatch[1].str();
                release.fileName = App::ZIP_FILENAME;
                releases.push_back(release);
            }
        }
    }

    return releases;
}
