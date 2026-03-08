#pragma once

#include "framework.h"
#include "Barkfluff.Installer.CPP.h"
#include <functional>

// ZIP extraction service using Windows Shell
class ZipExtractor
{
public:
    // Extract ZIP file to destination folder with progress callback
    bool Extract(
        const std::wstring& zipPath,
        const std::wstring& destPath,
        std::function<void(int percent, int filesExtracted, int totalFiles, double speed)> onProgress
    );

    // Get last error
    const std::wstring& GetLastError() const { return m_lastError; }

private:
    std::wstring m_lastError;

    // Count files in ZIP
    int CountFilesInZip(const std::wstring& zipPath);

    // Copy items with progress
    bool CopyItemsWithProgress(IShellFolder* pZipFolder, const std::wstring& destPath,
        int totalFiles, std::function<void(int percent, int filesExtracted, int totalFiles, double speed)> onProgress);
};
