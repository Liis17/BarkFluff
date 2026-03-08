#pragma once

#include "framework.h"
#include "Barkfluff.Installer.CPP.h"
#include "HttpClient.h"
#include "GitHubService.h"
#include "ZipExtractor.h"
#include "ProtocolService.h"
#include "ShortcutService.h"

// Main installer logic
class Installer
{
public:
    Installer();
    ~Installer();

    // Run installation
    bool Install(ProgressCallback* callback);

    // Run update
    bool Update(ProgressCallback* callback);

    // Get last error
    const std::wstring& GetLastError() const { return m_lastError; }

private:
    HttpClient* m_httpClient;
    GitHubService* m_gitHubService;
    ZipExtractor* m_zipExtractor;
    ProtocolService* m_protocolService;
    ShortcutService* m_shortcutService;
    std::wstring m_lastError;

    // Helper methods
    bool DownloadAndExtract(const ReleaseInfo& release, const std::wstring& installPath, ProgressCallback* callback);
    bool ConfigureSystemIntegration(const std::wstring& exePath, ProgressCallback* callback);
    bool LaunchApplication(const std::wstring& exePath, const std::wstring& args, bool silent);
    bool RequestAppClose();
};
