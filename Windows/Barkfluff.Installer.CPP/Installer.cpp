#include "Installer.h"

Installer::Installer()
{
    m_httpClient = new HttpClient();
    m_gitHubService = new GitHubService(m_httpClient);
    m_zipExtractor = new ZipExtractor();
    m_protocolService = new ProtocolService();
    m_shortcutService = new ShortcutService();
}

Installer::~Installer()
{
    delete m_shortcutService;
    delete m_protocolService;
    delete m_zipExtractor;
    delete m_gitHubService;
    delete m_httpClient;
}

bool Installer::Install(ProgressCallback* callback)
{
    m_lastError.clear();

    if (callback && callback->onInfo)
        callback->onInfo(L"Checking releases repository...");

    // Get latest release
    ReleaseInfo release = m_gitHubService->GetLatestStableRelease();
    if (release.buildNumber == 0)
    {
        m_lastError = m_gitHubService->GetLastError();
        if (m_lastError.empty()) m_lastError = L"No stable release found";
        if (callback && callback->onError)
            callback->onError(m_lastError.c_str());
        return false;
    }

    std::wstring statusMsg = L"Found release: " + release.tagName;
    if (callback && callback->onSuccess)
        callback->onSuccess(statusMsg.c_str());

    // Get install path
    std::wstring installPath = GetDefaultInstallPath();
    statusMsg = L"Installing to: " + installPath;
    if (callback && callback->onInfo)
        callback->onInfo(statusMsg.c_str());

    // Download and extract
    if (!DownloadAndExtract(release, installPath, callback))
    {
        return false;
    }

    // Configure system integration
    std::wstring exePath = GetBarkFluffExePath(installPath);
    if (!ConfigureSystemIntegration(exePath, callback))
    {
        // Continue even if system integration fails
    }

    if (callback && callback->onSuccess)
        callback->onSuccess(L"Installation completed successfully!");

    // Launch application
    LaunchApplication(exePath, L"", false);

    return true;
}

bool Installer::Update(ProgressCallback* callback)
{
    m_lastError.clear();

    if (callback && callback->onInfo)
        callback->onInfo(L"Checking for updates...");

    // Determine update path
    std::wstring updatePath;
    if (IsLocalInstallation())
    {
        updatePath = GetCurrentModulePath();
        if (callback && callback->onInfo)
            callback->onInfo(L"Local installation detected");
    }
    else
    {
        updatePath = GetDefaultInstallPath();
        if (callback && callback->onInfo)
            callback->onInfo(L"No local installation found");
    }

    // Get latest release
    ReleaseInfo release = m_gitHubService->GetLatestStableRelease();
    if (release.buildNumber == 0)
    {
        m_lastError = m_gitHubService->GetLastError();
        if (m_lastError.empty()) m_lastError = L"No stable release found";
        if (callback && callback->onError)
            callback->onError(m_lastError.c_str());
        return false;
    }

    std::wstring statusMsg = L"Found release: " + release.tagName;
    if (callback && callback->onSuccess)
        callback->onSuccess(statusMsg.c_str());

    // Request app close
    if (callback && callback->onInfo)
        callback->onInfo(L"Requesting application to close...");
    RequestAppClose();

    // Download and extract
    if (!DownloadAndExtract(release, updatePath, callback))
    {
        return false;
    }

    // Configure system integration
    std::wstring exePath = GetBarkFluffExePath(updatePath);
    if (!ConfigureSystemIntegration(exePath, callback))
    {
        // Continue even if system integration fails
    }

    if (callback && callback->onSuccess)
        callback->onSuccess(L"Update completed successfully!");

    // Launch application with update flag
    LaunchApplication(exePath, App::UPDATE_FLAG, false);

    return true;
}

bool Installer::DownloadAndExtract(const ReleaseInfo& release, const std::wstring& installPath, ProgressCallback* callback)
{
    // Download to temp
    std::wstring tempPath = std::filesystem::temp_directory_path().wstring();
    std::wstring zipPath = tempPath + L"\\" + release.fileName;

    if (callback && callback->onInfo)
        callback->onInfo(L"Downloading update...");

    // Progress wrapper for download
    auto downloadProgress = [callback](int percent, int64_t downloaded, int64_t total, double speed) {
        if (callback && callback->onProgress)
        {
            // Download is 50% of total progress
            int adjustedPercent = percent / 2;
            std::wstring status = L"Downloading... " + std::to_wstring(percent) + L"%";
            callback->onProgress(adjustedPercent, status.c_str());
        }
    };

    if (!m_httpClient->DownloadFile(release.downloadUrl, zipPath, downloadProgress))
    {
        m_lastError = m_httpClient->GetLastError();
        if (callback && callback->onError)
            callback->onError(m_lastError.c_str());
        return false;
    }

    if (callback && callback->onInfo)
        callback->onInfo(L"Extracting files...");

    // Progress wrapper for extraction
    auto extractProgress = [callback](int percent, int filesExtracted, int totalFiles, double speed) {
        if (callback && callback->onProgress)
        {
            // Extraction is 50% of total progress (50-100)
            int adjustedPercent = 50 + (percent / 2);
            std::wstring status = L"Extracting... " + std::to_wstring(percent) + L"% (" +
                std::to_wstring(filesExtracted) + L"/" + std::to_wstring(totalFiles) + L" files)";
            callback->onProgress(adjustedPercent, status.c_str());
        }
    };

    if (!m_zipExtractor->Extract(zipPath, installPath, extractProgress))
    {
        m_lastError = m_zipExtractor->GetLastError();
        if (callback && callback->onError)
            callback->onError(m_lastError.c_str());
        return false;
    }

    // Cleanup temp file
    DeleteFileW(zipPath.c_str());

    return true;
}

bool Installer::ConfigureSystemIntegration(const std::wstring& exePath, ProgressCallback* callback)
{
    if (callback && callback->onInfo)
        callback->onInfo(L"Configuring system integration...");

    bool success = true;

    // Register protocol
    if (!m_protocolService->RegisterProtocol(exePath))
    {
        m_lastError = m_protocolService->GetLastError();
        success = false;
    }

    // Create shortcut
    if (!m_shortcutService->CreateStartMenuShortcut(exePath))
    {
        if (m_lastError.empty())
            m_lastError = m_shortcutService->GetLastError();
        success = false;
    }

    return success;
}

bool Installer::LaunchApplication(const std::wstring& exePath, const std::wstring& args, bool silent)
{
    if (!std::filesystem::exists(exePath))
    {
        return false;
    }

    SHELLEXECUTEINFOW sei = {0};
    sei.cbSize = sizeof(sei);
    sei.lpFile = exePath.c_str();
    sei.lpParameters = args.c_str();
    sei.lpDirectory = std::filesystem::path(exePath).parent_path().c_str();
    sei.nShow = SW_SHOWNORMAL;
    sei.fMask = SEE_MASK_NOASYNC;

    return ShellExecuteExW(&sei) != FALSE;
}

bool Installer::RequestAppClose()
{
    SHELLEXECUTEINFOW sei = {0};
    sei.cbSize = sizeof(sei);
    sei.lpFile = App::CLOSE_URI;
    sei.nShow = SW_SHOWNORMAL;
    sei.fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI;

    ShellExecuteExW(&sei);

    // Wait for app to close
    Sleep(App::CLOSE_WAIT_MS);

    return true;
}
