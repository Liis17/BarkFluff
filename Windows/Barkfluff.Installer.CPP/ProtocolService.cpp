#include "ProtocolService.h"

bool ProtocolService::RegisterProtocol(const std::wstring& exePath)
{
    m_lastError.clear();

    // Check if already registered with correct path
    std::wstring registeredPath = GetRegisteredPath();
    if (!registeredPath.empty())
    {
        std::wstring normalizedExe = NormalizePath(exePath);
        std::wstring normalizedReg = NormalizePath(registeredPath);

        if (_wcsicmp(normalizedExe.c_str(), normalizedReg.c_str()) == 0)
        {
            // Already registered with correct path
            return true;
        }
    }

    // Create registry keys
    // HKEY_CLASSES_ROOT\bf
    HKEY hKey = nullptr;
    LONG result = RegCreateKeyExW(HKEY_CLASSES_ROOT, App::PROTOCOL, 0, nullptr, 0, KEY_WRITE, nullptr, &hKey, nullptr);
    if (result != ERROR_SUCCESS)
    {
        m_lastError = L"Failed to create registry key";
        return false;
    }

    // Set default value
    std::wstring desc = L"URL:BarkFluff Messenger Protocol";
    RegSetValueExW(hKey, nullptr, 0, REG_SZ, (const BYTE*)desc.c_str(), (DWORD)((desc.length() + 1) * sizeof(wchar_t)));

    // Set URL Protocol
    RegSetValueExW(hKey, L"URL Protocol", 0, REG_SZ, (const BYTE*)L"", sizeof(wchar_t));

    // DefaultIcon
    HKEY hIconKey = nullptr;
    result = RegCreateKeyExW(hKey, L"DefaultIcon", 0, nullptr, 0, KEY_WRITE, nullptr, &hIconKey, nullptr);
    if (result == ERROR_SUCCESS)
    {
        std::wstring iconPath = L"\"" + exePath + L"\",0";
        RegSetValueExW(hIconKey, nullptr, 0, REG_SZ, (const BYTE*)iconPath.c_str(), (DWORD)((iconPath.length() + 1) * sizeof(wchar_t)));
        RegCloseKey(hIconKey);
    }

    // shell\open\command
    HKEY hCmdKey = nullptr;
    result = RegCreateKeyExW(hKey, L"shell\\open\\command", 0, nullptr, 0, KEY_WRITE, nullptr, &hCmdKey, nullptr);
    if (result == ERROR_SUCCESS)
    {
        std::wstring cmd = L"\"" + exePath + L"\" \"%1\"";
        RegSetValueExW(hCmdKey, nullptr, 0, REG_SZ, (const BYTE*)cmd.c_str(), (DWORD)((cmd.length() + 1) * sizeof(wchar_t)));
        RegCloseKey(hCmdKey);
    }

    RegCloseKey(hKey);
    return true;
}

bool ProtocolService::UnregisterProtocol()
{
    m_lastError.clear();

    LONG result = RegDeleteTreeW(HKEY_CLASSES_ROOT, App::PROTOCOL);
    if (result != ERROR_SUCCESS && result != ERROR_FILE_NOT_FOUND)
    {
        m_lastError = L"Failed to delete registry key";
        return false;
    }

    return true;
}

bool ProtocolService::IsProtocolRegistered()
{
    HKEY hKey = nullptr;
    LONG result = RegOpenKeyExW(HKEY_CLASSES_ROOT, App::PROTOCOL, 0, KEY_READ, &hKey);
    if (result == ERROR_SUCCESS)
    {
        RegCloseKey(hKey);
        return true;
    }
    return false;
}

std::wstring ProtocolService::GetRegisteredPath()
{
    HKEY hKey = nullptr;
    LONG result = RegOpenKeyExW(HKEY_CLASSES_ROOT,
        (std::wstring(App::PROTOCOL) + L"\\shell\\open\\command").c_str(),
        0, KEY_READ, &hKey);

    if (result != ERROR_SUCCESS)
    {
        return L"";
    }

    wchar_t buffer[1024] = {0};
    DWORD bufferSize = sizeof(buffer);
    result = RegQueryValueExW(hKey, nullptr, nullptr, nullptr, (LPBYTE)buffer, &bufferSize);
    RegCloseKey(hKey);

    if (result != ERROR_SUCCESS)
    {
        return L"";
    }

    return ExtractPathFromCommand(buffer);
}

std::wstring ProtocolService::NormalizePath(const std::wstring& path)
{
    if (path.empty()) return L"";

    wchar_t buffer[MAX_PATH] = {0};
    DWORD len = GetFullPathNameW(path.c_str(), MAX_PATH, buffer, nullptr);
    if (len > 0)
    {
        std::wstring result(buffer);
        for (auto& c : result) c = towlower(c);
        return result;
    }

    std::wstring result = path;
    for (auto& c : result) c = towlower(c);
    return result;
}

std::wstring ProtocolService::ExtractPathFromCommand(const std::wstring& command)
{
    if (command.empty()) return L"";

    if (command[0] == L'"')
    {
        size_t endQuote = command.find(L'"', 1);
        if (endQuote != std::wstring::npos)
        {
            return command.substr(1, endQuote - 1);
        }
    }
    else
    {
        size_t spacePos = command.find(L' ');
        if (spacePos != std::wstring::npos)
        {
            return command.substr(0, spacePos);
        }
        return command;
    }

    return L"";
}
