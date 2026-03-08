#pragma once

#include "framework.h"
#include "Barkfluff.Installer.CPP.h"
#include "HttpClient.h"
#include <regex>

// GitHub releases service
class GitHubService
{
public:
    GitHubService(HttpClient* client);

    // Get latest stable release (Master or Release channel)
    ReleaseInfo GetLatestStableRelease();

    // Get last error
    const std::wstring& GetLastError() const { return m_lastError; }

private:
    HttpClient* m_httpClient;
    std::wstring m_lastError;

    // Parse releases from JSON response
    std::vector<ReleaseInfo> ParseReleases(const std::wstring& json);
};
