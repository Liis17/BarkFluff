@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ===============================================
echo   BarkFluff Android Release Keystore Generator
echo ===============================================
echo.

set KEYSTORE_NAME=barkfluff-release.jks
set ALIAS_NAME=barkfluff-release

if exist "%KEYSTORE_NAME%" (
    echo [x] Файл %KEYSTORE_NAME% уже существует в этой папке.
    set /p CONFIRM="Перезаписать? (y/n): "
    if /i not "!CONFIRM!"=="y" (
        echo Отменено.
        exit /b 1
    )
    del "%KEYSTORE_NAME%"
)

where keytool >nul 2>nul
if errorlevel 1 (
    echo [x] keytool не найден в PATH. Установи JDK и добавь его bin в PATH.
    exit /b 1
)

set /p STORE_PASS="Пароль keystore (мин. 6 символов, ввод виден на экране): "
set /p KEY_PASS="Пароль ключа (Enter = такой же, как у keystore): "
if "%KEY_PASS%"=="" set KEY_PASS=%STORE_PASS%

keytool -genkeypair -v ^
    -keystore "%KEYSTORE_NAME%" ^
    -alias "%ALIAS_NAME%" ^
    -keyalg RSA -keysize 4096 -validity 10950 ^
    -storetype JKS ^
    -storepass "%STORE_PASS%" ^
    -keypass "%KEY_PASS%" ^
    -dname "CN=BarkFluff, OU=BarkFluff, O=BarkFluff, L=Unknown, ST=Unknown, C=RU"

if errorlevel 1 (
    echo [x] Ошибка генерации keystore.
    exit /b 1
)

echo.
echo ===============================================
echo Keystore создан: %KEYSTORE_NAME%
echo Alias: %ALIAS_NAME%
echo.
echo ВАЖНО: сохрани оба пароля в менеджере паролей прямо сейчас.
echo Больше нигде они не хранятся, при потере ключ бесполезен.
echo.
echo Далее: keystore-to-base64.cmd — получить base64 для секрета
echo GitHub Actions ANDROID_RELEASE_STORE_B64.
echo ===============================================

endlocal
