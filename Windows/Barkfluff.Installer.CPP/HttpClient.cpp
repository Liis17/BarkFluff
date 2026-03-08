#include "HttpClient.h"
#include "Barkfluff.Installer.CPP.h"
#include <chrono>
#include <algorithm>

HttpClient::HttpClient() : m_hSession(nullptr)
{
    m_hSession = WinHttpOpen(
        App::USER_AGENT,
        WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
        WINHTTP_NO_PROXY_NAME,
        WINHTTP_NO_PROXY_BYPASS,
        0
    );
}

HttpClient::~HttpClient()
{
    if (m_hSession)
    {
        WinHttpCloseHandle(m_hSession);
    }
}

std::wstring HttpClient::FetchJson(const std::wstring& host, const std::wstring& path)
{
    if (!m_hSession)
    {
        m_lastError = L"Failed to initialize WinHTTP";
        return L"";
    }

    HINTERNET hConnect = WinHttpConnect(m_hSession, host.c_str(), INTERNET_DEFAULT_HTTPS_PORT, 0);
    if (!hConnect)
    {
        m_lastError = L"Failed to connect to server";
        return L"";
    }

    HINTERNET hRequest = WinHttpOpenRequest(
        hConnect,
        L"GET",
        path.c_str(),
        nullptr,
        WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES,
        WINHTTP_FLAG_SECURE
    );

    if (!hRequest)
    {
        WinHttpCloseHandle(hConnect);
        m_lastError = L"Failed to create request";
        return L"";
    }

    // Add headers
    std::wstring headers = L"User-Agent: " + std::wstring(App::USER_AGENT) + L"\r\n";
    WinHttpAddRequestHeaders(hRequest, headers.c_str(), -1, WINHTTP_ADDREQ_FLAG_ADD);

    BOOL bResult = WinHttpSendRequest(hRequest, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0);
    if (!bResult)
    {
        m_lastError = L"Failed to send request";
        WinHttpCloseHandle(hRequest);
        WinHttpCloseHandle(hConnect);
        return L"";
    }

    bResult = WinHttpReceiveResponse(hRequest, nullptr);
    if (!bResult)
    {
        m_lastError = L"Failed to receive response";
        WinHttpCloseHandle(hRequest);
        WinHttpCloseHandle(hConnect);
        return L"";
    }

    // Read data
    std::wstring response;
    DWORD dwSize = 0;
    DWORD dwDownloaded = 0;
    LPSTR pszOutBuffer;

    do
    {
        dwSize = 0;
        if (!WinHttpQueryDataAvailable(hRequest, &dwSize))
        {
            break;
        }

        pszOutBuffer = new char[dwSize + 1];
        if (!pszOutBuffer)
        {
            break;
        }

        ZeroMemory(pszOutBuffer, dwSize + 1);

        if (WinHttpReadData(hRequest, (LPVOID)pszOutBuffer, dwSize, &dwDownloaded))
        {
            // Convert to wide string
            int wideSize = MultiByteToWideChar(CP_UTF8, 0, pszOutBuffer, dwDownloaded, nullptr, 0);
            if (wideSize > 0)
            {
                wchar_t* wideBuffer = new wchar_t[wideSize + 1];
                MultiByteToWideChar(CP_UTF8, 0, pszOutBuffer, dwDownloaded, wideBuffer, wideSize);
                wideBuffer[wideSize] = L'\0';
                response += wideBuffer;
                delete[] wideBuffer;
            }
        }

        delete[] pszOutBuffer;
    } while (dwSize > 0);

    WinHttpCloseHandle(hRequest);
    WinHttpCloseHandle(hConnect);

    return response;
}

bool HttpClient::ParseUrl(const std::wstring& url, std::wstring& host, std::wstring& path, bool& https)
{
    https = true;
    std::wstring urlLower = url;
    for (auto& c : urlLower) c = towlower(c);

    size_t pos = 0;
    if (urlLower.find(L"https://") == 0)
    {
        https = true;
        pos = 8;
    }
    else if (urlLower.find(L"http://") == 0)
    {
        https = false;
        pos = 7;
    }
    else
    {
        m_lastError = L"Invalid URL scheme";
        return false;
    }

    size_t slashPos = url.find(L'/', pos);
    if (slashPos == std::wstring::npos)
    {
        host = url.substr(pos);
        path = L"/";
    }
    else
    {
        host = url.substr(pos, slashPos - pos);
        path = url.substr(slashPos);
    }

    return true;
}

bool HttpClient::DownloadFile(
    const std::wstring& url,
    const std::wstring& localPath,
    std::function<void(int percent, int64_t downloaded, int64_t total, double speed)> onProgress)
{
    std::wstring host, path;
    bool https;

    if (!ParseUrl(url, host, path, https))
    {
        return false;
    }

    if (!m_hSession)
    {
        m_lastError = L"Failed to initialize WinHTTP";
        return false;
    }

    HINTERNET hConnect = WinHttpConnect(
        m_hSession,
        host.c_str(),
        https ? INTERNET_DEFAULT_HTTPS_PORT : INTERNET_DEFAULT_HTTP_PORT,
        0
    );

    if (!hConnect)
    {
        m_lastError = L"Failed to connect to server";
        return false;
    }

    DWORD flags = https ? WINHTTP_FLAG_SECURE : 0;
    HINTERNET hRequest = WinHttpOpenRequest(
        hConnect,
        L"GET",
        path.c_str(),
        nullptr,
        WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES,
        flags
    );

    if (!hRequest)
    {
        WinHttpCloseHandle(hConnect);
        m_lastError = L"Failed to create request";
        return false;
    }

    std::wstring headers = L"User-Agent: " + std::wstring(App::USER_AGENT) + L"\r\n";
    WinHttpAddRequestHeaders(hRequest, headers.c_str(), -1, WINHTTP_ADDREQ_FLAG_ADD);

    BOOL bResult = WinHttpSendRequest(hRequest, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0);
    if (!bResult)
    {
        m_lastError = L"Failed to send request";
        WinHttpCloseHandle(hRequest);
        WinHttpCloseHandle(hConnect);
        return false;
    }

    bResult = WinHttpReceiveResponse(hRequest, nullptr);
    if (!bResult)
    {
        m_lastError = L"Failed to receive response";
        WinHttpCloseHandle(hRequest);
        WinHttpCloseHandle(hConnect);
        return false;
    }

    // Get content length
    int64_t contentLength = -1;
    DWORD dwBufferSize = sizeof(contentLength);
    DWORD dwIndex = 0;
    WinHttpQueryHeaders(hRequest,
        WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER64,
        WINHTTP_HEADER_NAME_BY_INDEX,
        &contentLength,
        &dwBufferSize,
        &dwIndex);

    // Create file
    HANDLE hFile = CreateFileW(localPath.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hFile == INVALID_HANDLE_VALUE)
    {
        m_lastError = L"Failed to create local file";
        WinHttpCloseHandle(hRequest);
        WinHttpCloseHandle(hConnect);
        return false;
    }

    // Download with progress
    auto startTime = std::chrono::steady_clock::now();
    int64_t totalDownloaded = 0;
    int64_t lastSpeedCalcBytes = 0;
    auto lastSpeedCalcTime = startTime;
    double currentSpeed = 0;

    const DWORD bufferSize = 8192;
    BYTE* buffer = new BYTE[bufferSize];
    DWORD dwBytesRead = 0;
    DWORD dwBytesWritten = 0;

    bool success = true;
    while (true)
    {
        DWORD dwSizeAvailable = 0;
        if (!WinHttpQueryDataAvailable(hRequest, &dwSizeAvailable))
        {
            m_lastError = L"Failed to query data";
            success = false;
            break;
        }

        if (dwSizeAvailable == 0)
            break;

        DWORD dwToRead = std::min(dwSizeAvailable, bufferSize);
        if (!WinHttpReadData(hRequest, buffer, dwToRead, &dwBytesRead))
        {
            m_lastError = L"Failed to read data";
            success = false;
            break;
        }

        if (dwBytesRead == 0)
            break;

        if (!WriteFile(hFile, buffer, dwBytesRead, &dwBytesWritten, nullptr) || dwBytesWritten != dwBytesRead)
        {
            m_lastError = L"Failed to write to file";
            success = false;
            break;
        }

        totalDownloaded += dwBytesRead;

        // Calculate speed every 200ms
        auto now = std::chrono::steady_clock::now();
        double elapsedSeconds = std::chrono::duration<double>(now - lastSpeedCalcTime).count();
        if (elapsedSeconds >= 0.2)
        {
            int64_t bytesInInterval = totalDownloaded - lastSpeedCalcBytes;
            currentSpeed = (bytesInInterval / elapsedSeconds) / (1024.0 * 1024.0); // MB/s
            lastSpeedCalcBytes = totalDownloaded;
            lastSpeedCalcTime = now;
        }

        if (onProgress && contentLength > 0)
        {
            int percent = static_cast<int>((totalDownloaded * 100) / contentLength);
            onProgress(percent, totalDownloaded, contentLength, currentSpeed);
        }
    }

    delete[] buffer;
    CloseHandle(hFile);
    WinHttpCloseHandle(hRequest);
    WinHttpCloseHandle(hConnect);

    if (!success)
    {
        DeleteFileW(localPath.c_str());
        return false;
    }

    if (onProgress && contentLength > 0)
    {
        onProgress(100, totalDownloaded, contentLength, currentSpeed);
    }

    return true;
}
