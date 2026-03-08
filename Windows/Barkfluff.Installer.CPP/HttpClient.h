#pragma once

#include "framework.h"
#include "Barkfluff.Installer.CPP.h"

// HTTP client for downloading files and fetching JSON
class HttpClient
{
public:
    HttpClient();
    ~HttpClient();

    // Fetch JSON string from URL
    std::wstring FetchJson(const std::wstring& host, const std::wstring& path);

    // Download file with progress callback
    bool DownloadFile(
        const std::wstring& url,
        const std::wstring& localPath,
        std::function<void(int percent, int64_t downloaded, int64_t total, double speed)> onProgress
    );

    // Get last error message
    const std::wstring& GetLastError() const { return m_lastError; }

private:
    HINTERNET m_hSession;
    std::wstring m_lastError;

    bool ParseUrl(const std::wstring& url, std::wstring& host, std::wstring& path, bool& https);
};
