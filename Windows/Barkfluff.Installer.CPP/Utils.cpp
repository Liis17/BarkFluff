#include "framework.h"
#include "Barkfluff.Installer.CPP.h"

// Parse command line arguments
CmdArgs ParseCommandLine(LPWSTR lpCmdLine)
{
    CmdArgs args;

    if (!lpCmdLine || wcslen(lpCmdLine) == 0)
    {
        args.mode = AppMode::Auto;
        return args;
    }

    std::wstring cmdLine(lpCmdLine);
    std::wstring lowerCmdLine = cmdLine;
    for (auto& c : lowerCmdLine) c = towlower(c);

    // Check for modes
    if (lowerCmdLine.find(L"-install") != std::wstring::npos ||
        lowerCmdLine.find(L"--install") != std::wstring::npos ||
        lowerCmdLine.find(L" -i") != std::wstring::npos)
    {
        args.mode = AppMode::Install;
    }
    else if (lowerCmdLine.find(L"-update") != std::wstring::npos ||
             lowerCmdLine.find(L"--update") != std::wstring::npos ||
             lowerCmdLine.find(L" -u") != std::wstring::npos)
    {
        args.mode = AppMode::Update;
    }
    else if (lowerCmdLine.find(L"-help") != std::wstring::npos ||
             lowerCmdLine.find(L"--help") != std::wstring::npos ||
             lowerCmdLine.find(L" -h") != std::wstring::npos ||
             lowerCmdLine.find(L"/?") != std::wstring::npos)
    {
        args.mode = AppMode::Help;
    }
    else
    {
        args.mode = AppMode::Auto;
    }

    // Check for silent mode
    if (lowerCmdLine.find(L"-silent") != std::wstring::npos ||
        lowerCmdLine.find(L"--silent") != std::wstring::npos ||
        lowerCmdLine.find(L" -s") != std::wstring::npos ||
        lowerCmdLine.find(L" -q") != std::wstring::npos ||
        lowerCmdLine.find(L"--quiet") != std::wstring::npos)
    {
        args.silent = true;
    }

    return args;
}

// Check if running as administrator
bool IsRunningAsAdmin()
{
    BOOL isAdmin = FALSE;
    PSID adminGroup = nullptr;

    SID_IDENTIFIER_AUTHORITY ntAuthority = SECURITY_NT_AUTHORITY;
    if (AllocateAndInitializeSid(&ntAuthority, 2, SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS,
        0, 0, 0, 0, 0, 0, &adminGroup))
    {
        if (!CheckTokenMembership(nullptr, adminGroup, &isAdmin))
        {
            isAdmin = FALSE;
        }
        FreeSid(adminGroup);
    }

    return isAdmin != FALSE;
}

// Restart as administrator
bool RestartAsAdmin(LPWSTR lpCmdLine)
{
    wchar_t exePath[MAX_PATH] = {0};
    GetModuleFileNameW(nullptr, exePath, MAX_PATH);

    SHELLEXECUTEINFOW sei = {0};
    sei.cbSize = sizeof(sei);
    sei.lpVerb = L"runas";
    sei.lpFile = exePath;
    sei.lpParameters = lpCmdLine;
    sei.nShow = SW_SHOWNORMAL;
    sei.fMask = SEE_MASK_NOASYNC;

    return ShellExecuteExW(&sei) != FALSE;
}

// Get default install path (%AppData%\BarkFluff)
std::wstring GetDefaultInstallPath()
{
    wchar_t appData[MAX_PATH] = {0};
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_APPDATA, nullptr, 0, appData)))
    {
        return std::wstring(appData) + L"\\" + App::APP_FOLDER;
    }
    return L"";
}

// Get current module path
std::wstring GetCurrentModulePath()
{
    wchar_t path[MAX_PATH] = {0};
    GetModuleFileNameW(nullptr, path, MAX_PATH);

    std::filesystem::path p(path);
    return p.parent_path().wstring();
}

// Check if Barkfluff.exe exists in current directory
bool IsLocalInstallation()
{
    std::wstring currentPath = GetCurrentModulePath();
    std::wstring exePath = currentPath + L"\\" + App::MAIN_EXE;
    return GetFileAttributesW(exePath.c_str()) != INVALID_FILE_ATTRIBUTES;
}

// Get BarkFluff executable path
std::wstring GetBarkFluffExePath(const std::wstring& installPath)
{
    return installPath + L"\\" + App::MAIN_EXE;
}
