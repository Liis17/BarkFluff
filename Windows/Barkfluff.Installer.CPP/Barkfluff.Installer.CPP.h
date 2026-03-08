#pragma once

#include "framework.h"

namespace App
{
    // Application info
    constexpr const wchar_t* APP_NAME = L"BarkFluff Installer";
    constexpr const wchar_t* APP_FOLDER = L"BarkFluff";
    constexpr const wchar_t* MAIN_EXE = L"Barkfluff.exe";
    constexpr const wchar_t* PROTOCOL = L"bf";
    constexpr const wchar_t* APP_USER_MODEL_ID = L"BarkFluff.Messenger";

    // GitHub API
    constexpr const wchar_t* GITHUB_RELEASES_URL = L"/repos/Liis17/BarkFluff.Releases/releases";
    constexpr const wchar_t* GITHUB_HOST = L"api.github.com";
    constexpr const wchar_t* USER_AGENT = L"BarkFluff-Updater-CPP";

    // Update
    constexpr const wchar_t* CLOSE_URI = L"bf://closetoupdate";
    constexpr const wchar_t* UPDATE_FLAG = L"--successfulupdate";
    constexpr const wchar_t* ZIP_FILENAME = L"wpf-build.zip";
    constexpr int CLOSE_WAIT_MS = 3000;
}

// Release info structure
struct ReleaseInfo
{
    std::wstring tagName;
    std::wstring version;
    std::wstring channel;
    int buildNumber = 0;
    std::wstring downloadUrl;
    std::wstring fileName;
};

// App mode
enum class AppMode
{
    Auto,       // Auto-detect: install or update
    Install,    // Fresh install
    Update,     // Update existing
    Help        // Show help
};

// Parsed command line arguments
struct CmdArgs
{
    AppMode mode = AppMode::Auto;
    bool silent = false;
};

// UI callbacks
struct ProgressCallback
{
    std::function<void(int percent, const wchar_t* status)> onProgress;
    std::function<void(const wchar_t* message)> onSuccess;
    std::function<void(const wchar_t* error)> onError;
    std::function<void(const wchar_t* info)> onInfo;
    std::function<void()> onComplete;
};

// Functions declarations
CmdArgs ParseCommandLine(LPWSTR lpCmdLine);
bool IsRunningAsAdmin();
bool RestartAsAdmin(LPWSTR lpCmdLine);
std::wstring GetDefaultInstallPath();
std::wstring GetCurrentModulePath();
bool IsLocalInstallation();
std::wstring GetBarkFluffExePath(const std::wstring& installPath);
