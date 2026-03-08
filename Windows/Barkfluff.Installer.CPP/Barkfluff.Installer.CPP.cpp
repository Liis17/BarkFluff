// Barkfluff.Installer.CPP.cpp : Defines the entry point for the application.
//

#include "framework.h"
#include "Barkfluff.Installer.CPP.h"
#include "Resource.h"
#include "Installer.h"

#define MAX_LOADSTRING 100

// Global Variables:
HINSTANCE g_hInst = nullptr;
HWND g_hDlg = nullptr;
HWND g_hProgressBar = nullptr;
HWND g_hStatusText = nullptr;
HWND g_hProgressLabel = nullptr;
std::atomic<bool> g_isWorking(false);
std::wstring g_lastError;

// Forward declarations
ATOM                MyRegisterClass(HINSTANCE hInstance);
BOOL                InitInstance(HINSTANCE, int);
LRESULT CALLBACK    WndProc(HWND, UINT, WPARAM, LPARAM);
INT_PTR CALLBACK    MainDlgProc(HWND, UINT, WPARAM, LPARAM);
void                RunInstaller(AppMode mode, bool silent);
void                UpdateUIProgress(int percent, const wchar_t* status);
void                UpdateUISuccess(const wchar_t* message);
void                UpdateUIError(const wchar_t* error);
void                UpdateUIInfo(const wchar_t* info);
void                SetUIMode(bool working);

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
                      _In_opt_ HINSTANCE hPrevInstance,
                      _In_ LPWSTR    lpCmdLine,
                      _In_ int       nCmdShow)
{
    UNREFERENCED_PARAMETER(hPrevInstance);
    UNREFERENCED_PARAMETER(lpCmdLine);

    // Initialize common controls
    INITCOMMONCONTROLSEX icc = {0};
    icc.dwSize = sizeof(icc);
    icc.dwICC = ICC_WIN95_CLASSES;
    InitCommonControlsEx(&icc);

    // Parse command line
    CmdArgs args = ParseCommandLine(lpCmdLine);

    // Check admin rights
    if (!IsRunningAsAdmin())
    {
        MessageBoxW(nullptr,
            L"This application requires Administrator privileges.\n"
            L"Please run as Administrator.",
            App::APP_NAME,
            MB_OK | MB_ICONERROR);

        // Try to restart as admin
        RestartAsAdmin(lpCmdLine);
        return 1;
    }

    // Auto mode: detect install or update
    if (args.mode == AppMode::Auto)
    {
        if (IsLocalInstallation())
        {
            args.mode = AppMode::Update;
        }
        else
        {
            args.mode = AppMode::Install;
        }
    }

    // Store instance handle
    g_hInst = hInstance;

    // Create main dialog
    g_hDlg = CreateDialogParam(hInstance, MAKEINTRESOURCE(IDD_MAIN_DIALOG), nullptr, MainDlgProc, 0);
    if (!g_hDlg)
    {
        MessageBoxW(nullptr, L"Failed to create dialog", App::APP_NAME, MB_OK | MB_ICONERROR);
        return 1;
    }

    // Show window
    ShowWindow(g_hDlg, nCmdShow);
    UpdateWindow(g_hDlg);

    // Start installer in background thread
    std::thread installerThread(RunInstaller, args.mode, args.silent);
    installerThread.detach();

    // Message loop
    MSG msg;
    while (GetMessage(&msg, nullptr, 0, 0))
    {
        if (!IsDialogMessage(g_hDlg, &msg))
        {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }
    }

    return (int)msg.wParam;
}

INT_PTR CALLBACK MainDlgProc(HWND hDlg, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
    case WM_INITDIALOG:
        {
            // Center window
            RECT rcDesktop, rcDlg;
            GetWindowRect(GetDesktopWindow(), &rcDesktop);
            GetWindowRect(hDlg, &rcDlg);

            int x = (rcDesktop.right - rcDesktop.left - (rcDlg.right - rcDlg.left)) / 2;
            int y = (rcDesktop.bottom - rcDesktop.top - (rcDlg.bottom - rcDlg.top)) / 2;

            SetWindowPos(hDlg, nullptr, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);

            // Get control handles
            g_hProgressBar = GetDlgItem(hDlg, IDC_PROGRESS_BAR);
            g_hStatusText = GetDlgItem(hDlg, IDC_STATUS_TEXT);
            g_hProgressLabel = GetDlgItem(hDlg, IDC_PROGRESS_LABEL);

            // Set window title
            SetWindowTextW(hDlg, App::APP_NAME);

            // Initialize progress bar
            SendMessage(g_hProgressBar, PBM_SETRANGE, 0, MAKELPARAM(0, 100));
            SendMessage(g_hProgressBar, PBM_SETPOS, 0, 0);

            // Set initial status
            SetWindowTextW(g_hStatusText, L"Initializing...");
            SetWindowTextW(g_hProgressLabel, L"0%");

            // Set dialog icon
            HICON hIcon = LoadIcon(g_hInst, MAKEINTRESOURCE(IDI_BARKFLUFFINSTALLER));
            SendMessage(hDlg, WM_SETICON, ICON_BIG, (LPARAM)hIcon);
            SendMessage(hDlg, WM_SETICON, ICON_SMALL, (LPARAM)hIcon);
        }
        return (INT_PTR)TRUE;

    case WM_COMMAND:
        switch (LOWORD(wParam))
        {
        case IDCANCEL:
        case IDM_EXIT:
            if (g_isWorking)
            {
                MessageBoxW(hDlg, L"Please wait for the operation to complete.", App::APP_NAME, MB_OK | MB_ICONWARNING);
                return (INT_PTR)TRUE;
            }
            DestroyWindow(hDlg);
            PostQuitMessage(0);
            return (INT_PTR)TRUE;

        case IDM_ABOUT:
            MessageBoxW(hDlg,
                L"BarkFluff Installer\n\n"
                L"Version 1.0\n"
                L"© 2024 BarkFluff Team",
                L"About",
                MB_OK | MB_ICONINFORMATION);
            return (INT_PTR)TRUE;
        }
        break;

    case WM_CLOSE:
        if (g_isWorking)
        {
            MessageBoxW(hDlg, L"Please wait for the operation to complete.", App::APP_NAME, MB_OK | MB_ICONWARNING);
            return (INT_PTR)TRUE;
        }
        DestroyWindow(hDlg);
        PostQuitMessage(0);
        return (INT_PTR)TRUE;

    case WM_DESTROY:
        PostQuitMessage(0);
        return (INT_PTR)TRUE;
    }
    return (INT_PTR)FALSE;
}

void RunInstaller(AppMode mode, bool silent)
{
    g_isWorking = true;
    SetUIMode(true);

    Installer installer;
    ProgressCallback callback;

    callback.onProgress = [](int percent, const wchar_t* status) {
        UpdateUIProgress(percent, status);
    };

    callback.onSuccess = [](const wchar_t* message) {
        UpdateUISuccess(message);
    };

    callback.onError = [](const wchar_t* error) {
        UpdateUIError(error);
    };

    callback.onInfo = [](const wchar_t* info) {
        UpdateUIInfo(info);
    };

    bool success = false;
    if (mode == AppMode::Install)
    {
        UpdateUIInfo(L"Starting installation...");
        success = installer.Install(&callback);
    }
    else if (mode == AppMode::Update)
    {
        UpdateUIInfo(L"Starting update...");
        success = installer.Update(&callback);
    }

    g_isWorking = false;
    SetUIMode(false);

    if (success)
    {
        UpdateUISuccess(mode == AppMode::Install ? L"Installation completed!" : L"Update completed!");
        UpdateUIProgress(100, L"Done");

        // Auto-close after success in silent mode
        if (silent)
        {
            Sleep(1000);
            PostMessage(g_hDlg, WM_CLOSE, 0, 0);
        }
    }
    else
    {
        std::wstring error = installer.GetLastError();
        if (error.empty()) error = L"Unknown error";
        UpdateUIError(error.c_str());
    }
}

void UpdateUIProgress(int percent, const wchar_t* status)
{
    if (g_hProgressBar)
    {
        SendMessage(g_hProgressBar, PBM_SETPOS, percent, 0);
    }
    if (g_hProgressLabel)
    {
        std::wstring text = std::to_wstring(percent) + L"%";
        SetWindowTextW(g_hProgressLabel, text.c_str());
    }
    if (g_hStatusText && status)
    {
        SetWindowTextW(g_hStatusText, status);
    }
}

void UpdateUISuccess(const wchar_t* message)
{
    if (g_hStatusText && message)
    {
        std::wstring text = L"[+] " + std::wstring(message);
        SetWindowTextW(g_hStatusText, text.c_str());
    }
}

void UpdateUIError(const wchar_t* error)
{
    if (g_hStatusText && error)
    {
        std::wstring text = L"[X] Error: " + std::wstring(error);
        SetWindowTextW(g_hStatusText, text.c_str());
    }
}

void UpdateUIInfo(const wchar_t* info)
{
    if (g_hStatusText && info)
    {
        std::wstring text = L"[i] " + std::wstring(info);
        SetWindowTextW(g_hStatusText, text.c_str());
    }
}

void SetUIMode(bool working)
{
    // Could disable close button, etc.
    // For now just a placeholder
}
