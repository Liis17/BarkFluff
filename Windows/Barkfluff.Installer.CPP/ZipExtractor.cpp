#include "ZipExtractor.h"
#include <chrono>
#include <shobjidl.h>
#include <algorithm>

bool ZipExtractor::Extract(
    const std::wstring& zipPath,
    const std::wstring& destPath,
    std::function<void(int percent, int filesExtracted, int totalFiles, double speed)> onProgress)
{
    m_lastError.clear();

    // Ensure destination exists
    if (!CreateDirectoryW(destPath.c_str(), nullptr))
    {
        if (GetLastError() != ERROR_ALREADY_EXISTS)
        {
            m_lastError = L"Failed to create destination directory";
            return false;
        }
    }

    // Initialize COM
    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    bool needCoUninitialize = SUCCEEDED(hr);

    bool success = false;

    // Create Shell object
    IShellDispatch* pShell = nullptr;
    hr = CoCreateInstance(CLSID_Shell, nullptr, CLSCTX_INPROC_SERVER, IID_IShellDispatch, (void**)&pShell);

    if (FAILED(hr))
    {
        m_lastError = L"Failed to create Shell object";
        if (needCoUninitialize) CoUninitialize();
        return false;
    }

    // Get ZIP folder
    Folder* pZipFolder = nullptr;
    VARIANT varZipPath;
    VariantInit(&varZipPath);
    varZipPath.vt = VT_BSTR;
    varZipPath.bstrVal = SysAllocString(zipPath.c_str());

    hr = pShell->NameSpace(varZipPath, &pZipFolder);
    VariantClear(&varZipPath);

    if (FAILED(hr) || !pZipFolder)
    {
        m_lastError = L"Failed to open ZIP file";
        pShell->Release();
        if (needCoUninitialize) CoUninitialize();
        return false;
    }

    // Get destination folder
    Folder* pDestFolder = nullptr;
    VARIANT varDestPath;
    VariantInit(&varDestPath);
    varDestPath.vt = VT_BSTR;
    varDestPath.bstrVal = SysAllocString(destPath.c_str());

    hr = pShell->NameSpace(varDestPath, &pDestFolder);
    VariantClear(&varDestPath);

    if (FAILED(hr) || !pDestFolder)
    {
        m_lastError = L"Failed to open destination folder";
        pZipFolder->Release();
        pShell->Release();
        if (needCoUninitialize) CoUninitialize();
        return false;
    }

    // Count items
    FolderItems* pItems = nullptr;
    hr = pZipFolder->Items(&pItems);
    if (FAILED(hr) || !pItems)
    {
        m_lastError = L"Failed to get ZIP items";
        pDestFolder->Release();
        pZipFolder->Release();
        pShell->Release();
        if (needCoUninitialize) CoUninitialize();
        return false;
    }

    long itemCount = 0;
    pItems->get_Count(&itemCount);

    if (itemCount == 0)
    {
        m_lastError = L"ZIP archive is empty";
        pItems->Release();
        pDestFolder->Release();
        pZipFolder->Release();
        pShell->Release();
        if (needCoUninitialize) CoUninitialize();
        return false;
    }

    // Report initial progress
    if (onProgress)
    {
        onProgress(0, 0, static_cast<int>(itemCount), 0);
    }

    // Copy all items from ZIP to destination
    // Using CopyHere with options: 4 = No progress, 16 = Yes to all, 512 = No confirm
    VARIANT varOptions;
    VariantInit(&varOptions);
    varOptions.vt = VT_I4;
    varOptions.lVal = 4 | 16 | 512; // No UI, Yes to all, No confirm

    VARIANT varItems;
    VariantInit(&varItems);
    varItems.vt = VT_DISPATCH;
    varItems.pdispVal = pItems;

    hr = pDestFolder->CopyHere(varItems, varOptions);

    VariantClear(&varItems);
    VariantClear(&varOptions);

    success = SUCCEEDED(hr);

    if (success)
    {
        // Wait for extraction to complete
        // CopyHere is async, so we need to poll
        auto startTime = std::chrono::steady_clock::now();
        int lastFileCount = 0;
        auto lastSpeedCalcTime = startTime;
        double currentSpeed = 0;

        while (true)
        {
            Sleep(100);

            // Count files in destination
            int currentFileCount = 0;
            WIN32_FIND_DATAW findData;
            std::wstring searchPath = destPath + L"\\*";
            HANDLE hFind = FindFirstFileW(searchPath.c_str(), &findData);
            if (hFind != INVALID_HANDLE_VALUE)
            {
                do
                {
                    if (wcscmp(findData.cFileName, L".") != 0 && wcscmp(findData.cFileName, L"..") != 0)
                    {
                        currentFileCount++;
                    }
                } while (FindNextFileW(hFind, &findData));
                FindClose(hFind);
            }

            // Calculate speed
            auto now = std::chrono::steady_clock::now();
            double elapsedSeconds = std::chrono::duration<double>(now - lastSpeedCalcTime).count();
            if (elapsedSeconds >= 0.2)
            {
                int filesInInterval = currentFileCount - lastFileCount;
                currentSpeed = elapsedSeconds > 0 ? filesInInterval / elapsedSeconds : 0;
                lastFileCount = currentFileCount;
                lastSpeedCalcTime = now;
            }

            if (onProgress)
            {
                int percent = std::min(99, static_cast<int>((currentFileCount * 100) / itemCount));
                onProgress(percent, currentFileCount, static_cast<int>(itemCount), currentSpeed);
            }

            // Check if extraction complete
            if (currentFileCount >= itemCount)
            {
                break;
            }

            // Timeout after 5 minutes
            if (std::chrono::duration_cast<std::chrono::seconds>(now - startTime).count() > 300)
            {
                m_lastError = L"Extraction timeout";
                success = false;
                break;
            }
        }
    }
    else
    {
        m_lastError = L"Failed to extract files";
    }

    pItems->Release();
    pDestFolder->Release();
    pZipFolder->Release();
    pShell->Release();

    if (needCoUninitialize) CoUninitialize();

    if (success && onProgress)
    {
        onProgress(100, static_cast<int>(itemCount), static_cast<int>(itemCount), 0);
    }

    return success;
}
