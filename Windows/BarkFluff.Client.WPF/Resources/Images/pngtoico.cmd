@echo off
setlocal

if "%~1" == "" (
    echo.
    echo Перетащите PNG файл на этот скрипт.
    echo.
    pause
    exit /b 1
)

set "FFMPEG_PATH=ffmpeg"

set "INPUT_PNG=%~1"

for %%i in ("%INPUT_PNG%") do set "FILENAME_NO_EXT=%%~ni"

for %%i in ("%INPUT_PNG%") do set "OUTPUT_DIR=%%~dpi"

set "OUTPUT_ICO=%OUTPUT_DIR%%FILENAME_NO_EXT%.ico"

where "%FFMPEG_PATH%" >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo Ошибка: FFmpeg не найден.
    echo Пожалуйста, убедитесь, что FFmpeg установлен и доступен в PATH,
    echo или укажите полный путь к ffmpeg.exe в переменной FFMPEG_PATH внутри скрипта.
    echo.
    pause
    exit /b 1
)

echo.
echo Конвертирование "%INPUT_PNG%" в ICO...
echo Масштабирование изображения до 256x256 пикселей для совместимости с ICO...

"%FFMPEG_PATH%" -i "%INPUT_PNG%" -vf scale=256:256 "%OUTPUT_ICO%"

if %errorlevel% equ 0 (
    echo.
    echo Успешно сконвертировано в "%OUTPUT_ICO%"
) else (
    echo.
    echo Произошла ошибка при конвертации.
    echo Пожалуйста, проверьте консольный вывод FFmpeg выше для получения подробностей.
    pause
)

echo.
timeout 2
endlocal
exit /b 0