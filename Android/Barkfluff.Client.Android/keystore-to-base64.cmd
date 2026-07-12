@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

set KEYSTORE_NAME=barkfluff-release.jks

if not exist "%KEYSTORE_NAME%" (
    echo [x] Файл %KEYSTORE_NAME% не найден в этой папке.
    echo     Сначала запусти generate-keystore.cmd
    exit /b 1
)

set "TMPFILE=%TEMP%\barkfluff-release-b64-%RANDOM%.tmp"
certutil -encode "%KEYSTORE_NAME%" "%TMPFILE%" >nul

set "OUT="
for /f "usebackq delims=" %%A in ("%TMPFILE%") do (
    echo %%A | findstr /c:"CERTIFICATE" >nul
    if errorlevel 1 set "OUT=!OUT!%%A"
)

del "%TMPFILE%" >nul 2>nul

echo ===============================================
echo   Base64 для секрета GitHub Actions ANDROID_RELEASE_STORE_B64
echo   Скопируй строку целиком (без пробелов по краям)
echo ===============================================
echo.
echo !OUT!
echo.
echo ===============================================

endlocal
