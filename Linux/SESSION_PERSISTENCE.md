# Session Persistence Implementation

## Overview

Реализовано сохранение состояния авторизации, чтобы не вводить логин и пароль при каждом запуске.

## New Components

### 1. AppSettings (`src/Storage/AppSettings.h/cpp`)
- Singleton для хранения настроек приложения
- Сохраняет последний сервер (host, port, name)
- Использует QSettings для хранения в конфиге Qt

### 2. SessionManager (`src/Services/SessionManager.h/cpp`)
- Singleton для управления сессией
- Шифрует и сохраняет токены (access + refresh)
- PIN-защита для расшифровки сессии
- Автоматическое обновление access token через refresh token
- Хранит последний логин для удобства

## User Flow

### Первый вход
1. Пользователь выбирает сервер
2. Вводит логин и пароль
3. После успешной аутентификации предлагается создать PIN
4. Сессия сохраняется в зашифрованном виде

### Последующие запуски
1. Приложение проверяет наличие сохранённой сессии
2. Если сессия есть → показывает диалог ввода PIN
3. После ввода PIN:
   - Расшифровывает сессию
   - Проверяет валидность access token
   - Если истёк — обновляет через refresh token
   - Если refresh тоже истёк — просит заново войти (логин предзаполнен)
4. При успехе — сразу открывает мессенджер

### Выход из аккаунта (Logout)
- Удаляет сохранённую сессию
- При следующем запуске — выбор сервера с начала

## Security

- Токены шифруются с использованием AES-256 через SecureStorage
- PIN используется как ключ для расшифровки
- PIN НЕ хранится в системе (только hash для проверки)
- При неверном PIN — доступ к сессии невозможен

## Files Modified

- `src/Storage/AppSettings.h` (new)
- `src/Storage/AppSettings.cpp` (new)
- `src/Services/SessionManager.h` (new)
- `src/Services/SessionManager.cpp` (new)
- `src/UI/MainWindow.h` (modified)
- `src/UI/MainWindow.cpp` (modified)
- `src/UI/LoginPage.h` (modified)
- `src/UI/LoginPage.cpp` (modified)
- `CMakeLists.txt` (modified)

## Usage

```cpp
// Initialize with PIN (first time or unlock)
SessionManager::instance().initializeWithPin(pin);

// Save session
SessionManager::instance().saveSession(session, serverConfig, login);

// Restore session
auto session = SessionManager::instance().restoreSession();

// Check if session exists
if (SessionManager::instance().hasStoredSession()) {
    // Show PIN dialog
}

// Clear session on logout
SessionManager::instance().clearSession();