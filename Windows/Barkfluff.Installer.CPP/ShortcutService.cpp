#include "ShortcutService.h"
#include <shlobj.h>
#include <propkey.h>
#include <propvarutil.h>

#pragma comment(lib, "propsys.lib")

bool ShortcutService::CreateStartMenuShortcut(const std::wstring& targetPath)
{
    m_lastError.clear();

    // Get Start Menu Programs path
    wchar_t startMenuPath[MAX_PATH] = {0};
    HRESULT hr = SHGetFolderPathW(nullptr, CSIDL_PROGRAMS, nullptr, 0, startMenuPath);
    if (FAILED(hr))
    {
        m_lastError = L"Failed to get Start Menu path";
        return false;
    }

    std::wstring shortcutPath = std::wstring(startMenuPath) + L"\\BarkFluff.lnk";

    // Check if shortcut already exists with correct path
    if (GetFileAttributesW(shortcutPath.c_str()) != INVALID_FILE_ATTRIBUTES)
    {
        std::wstring existingPath = GetShortcutTargetPath(shortcutPath);
        if (!existingPath.empty())
        {
            std::wstring normalizedTarget = NormalizePath(targetPath);
            std::wstring normalizedExisting = NormalizePath(existingPath);

            if (_wcsicmp(normalizedTarget.c_str(), normalizedExisting.c_str()) == 0)
            {
                // Shortcut already up to date
                return true;
            }
        }
    }

    return CreateShortcut(shortcutPath, targetPath, App::APP_USER_MODEL_ID);
}

bool ShortcutService::RemoveStartMenuShortcut()
{
    m_lastError.clear();

    wchar_t startMenuPath[MAX_PATH] = {0};
    HRESULT hr = SHGetFolderPathW(nullptr, CSIDL_PROGRAMS, nullptr, 0, startMenuPath);
    if (FAILED(hr))
    {
        m_lastError = L"Failed to get Start Menu path";
        return false;
    }

    std::wstring shortcutPath = std::wstring(startMenuPath) + L"\\BarkFluff.lnk";

    if (GetFileAttributesW(shortcutPath.c_str()) != INVALID_FILE_ATTRIBUTES)
    {
        if (!DeleteFileW(shortcutPath.c_str()))
        {
            m_lastError = L"Failed to delete shortcut";
            return false;
        }
    }

    return true;
}

std::wstring ShortcutService::GetShortcutTargetPath(const std::wstring& shortcutPath)
{
    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    bool needCoUninitialize = SUCCEEDED(hr);

    IShellLinkW* pShellLink = nullptr;
    hr = CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_IShellLinkW, (void**)&pShellLink);

    if (FAILED(hr))
    {
        if (needCoUninitialize) CoUninitialize();
        return L"";
    }

    IPersistFile* pPersistFile = nullptr;
    hr = pShellLink->QueryInterface(IID_IPersistFile, (void**)&pPersistFile);
    if (FAILED(hr))
    {
        pShellLink->Release();
        if (needCoUninitialize) CoUninitialize();
        return L"";
    }

    hr = pPersistFile->Load(shortcutPath.c_str(), STGM_READ);
    if (FAILED(hr))
    {
        pPersistFile->Release();
        pShellLink->Release();
        if (needCoUninitialize) CoUninitialize();
        return L"";
    }

    wchar_t path[MAX_PATH] = {0};
    hr = pShellLink->GetPath(path, MAX_PATH, nullptr, 0);

    pPersistFile->Release();
    pShellLink->Release();
    if (needCoUninitialize) CoUninitialize();

    return SUCCEEDED(hr) ? path : L"";
}

std::wstring ShortcutService::NormalizePath(const std::wstring& path)
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

bool ShortcutService::CreateShortcut(const std::wstring& shortcutPath, const std::wstring& targetPath, const std::wstring& appUserModelId)
{
    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    bool needCoUninitialize = SUCCEEDED(hr);

    IShellLinkW* pShellLink = nullptr;
    hr = CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_IShellLinkW, (void**)&pShellLink);

    if (FAILED(hr))
    {
        m_lastError = L"Failed to create ShellLink";
        if (needCoUninitialize) CoUninitialize();
        return false;
    }

    // Set shortcut properties
    pShellLink->SetPath(targetPath.c_str());
    pShellLink->SetDescription(L"BarkFluff Messenger");

    // Get working directory
    std::filesystem::path p(targetPath);
    std::wstring workingDir = p.parent_path().wstring();
    pShellLink->SetWorkingDirectory(workingDir.c_str());

    // Set AppUserModelID for proper taskbar grouping
    IPropertyStore* pPropStore = nullptr;
    hr = pShellLink->QueryInterface(IID_IPropertyStore, (void**)&pPropStore);
    if (SUCCEEDED(hr))
    {
        PROPVARIANT propVar;
        hr = InitPropVariantFromString(appUserModelId.c_str(), &propVar);
        if (SUCCEEDED(hr))
        {
            hr = pPropStore->SetValue(PKEY_AppUserModel_ID, propVar);
            pPropStore->Commit();
            PropVariantClear(&propVar);
        }
        pPropStore->Release();
    }

    // Save shortcut
    IPersistFile* pPersistFile = nullptr;
    hr = pShellLink->QueryInterface(IID_IPersistFile, (void**)&pPersistFile);
    if (FAILED(hr))
    {
        pShellLink->Release();
        if (needCoUninitialize) CoUninitialize();
        m_lastError = L"Failed to get PersistFile interface";
        return false;
    }

    hr = pPersistFile->Save(shortcutPath.c_str(), TRUE);

    pPersistFile->Release();
    pShellLink->Release();
    if (needCoUninitialize) CoUninitialize();

    if (FAILED(hr))
    {
        m_lastError = L"Failed to save shortcut";
        return false;
    }

    return true;
}
