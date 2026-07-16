# BarkFluffQt (Linux)

Кроссплатформенный клиент на Qt 6 / C++20 с gRPC.

> UI/UX спецификация всех экранов и сценариев: [[Клиенты/DesignDocument]] (источник: `dd.md`)

Расположение: `Linux/`

## Tech Stack

| Компонент | Технология |
|-----------|------------|
| GUI Framework | Qt 6 (Widgets, Network, Svg, Concurrent, Multimedia, MultimediaWidgets, DBus) |
| Language | C++20 |
| Build System | CMake 3.20+ |
| RPC | gRPC + Protobuf |
| Encryption | OpenSSL |
| Optional | KDE Frameworks 6 (KConfig) |

> `Qt6::DBus` — для интеграции с системными уведомлениями freedesktop. `MultimediaWidgets` — для встроенного видео-плеера.

## Сборка

```bash
./install_deps.sh     # Debian/Ubuntu
mkdir build && cd build
cmake ..
make -j$(nproc)
./BarkFluffQt
```

## Архитектура (слоистая)

```
src/
├── Connection/     # gRPC клиенты
├── Models/         # Модели данных
├── UI/
│   ├── *.h/.cpp    # Страницы: MainWindow, LoginPage, RegisterPage, ServerSelectPage,
│   │               #   MessengerPage, ProfilePage, UserProfileView, PinUnlockDialog
│   ├── Settings/   # SettingsPage + General/Security/Sessions/Storage/AboutWidget
│   └── Widgets/    # ~13 переиспользуемых виджетов (аватар, эмодзи-пикер, вложения, бейджи, ...)
├── Services/       # SessionManager (singleton, PIN-защита, автовосстановление сессии), FileCacheService, NotificationService (singleton, D-Bus + tray fallback)
├── Storage/        # SecureStorage (AES-256 шифрование токенов), AppSettings
└── Utils/          # ErrorHandler, Validators, MessageGrouper, ...
```

**Сохранение сессии** (см. `Linux/SESSION_PERSISTENCE.md`): `SessionManager` — singleton с PIN-блокировкой (`PinUnlockDialog`), шифрует токены через `SecureStorage` (AES-256), при старте пытается восстановить сессию и молча обновить access token через refresh token.

## gRPC Clients (Connection Layer)

Наследование от `GrpcClient` (базовый с `createContext(accessToken)`):

| Класс | Назначение |
|-------|------------|
| `NavigatorClient` | Серверы |
| `BeaconClient` | Heartbeat |
| `IdentityClient` | Авторизация |
| `MessagesClient` | Чаты, сообщения |
| `FilesClient` | Файлы |
| `UsersClient` | Профили |
| `UpdatesClient` | Server-sent events |
| `OnlinerClient` | Онлайн-статусы |

## Паттерны

- **Singleton**: SessionManager, FileCacheService, NotificationService (D-Bus `org.freedesktop.Notifications` + `QSystemTrayIcon` fallback; отсюда `Qt6::DBus` в стеке)
- **Template Method**: GrpcClient → наследники
- **Observer**: Qt Signals/Slots
- **Optimistic UI**: `Message::createPending()` с отрицательным ID, `markAsSent/markAsFailed`
- **Factory Methods**: `MessageListItem::dateSeparator()`, `MessageListItem::messageItem()`

## Code Style

- Namespace: `BarkFluff`
- `#pragma once`
- Классы: PascalCase; методы: camelCase; члены: `camelCase_` (trailing underscore)
- Const references для параметров; const методы
- Qt containers (QList, QMap) для Qt кода; STL (unique_ptr, shared_ptr) для gRPC
- New-style connects: `connect(src, &Src::signal, dst, &Dst::slot)`
- Doxygen на русском языке

## Аутентификация

Base64-encoded metadata в каждом запросе через `GrpcClient::createContext()`:
```cpp
context->AddMetadata("x-device-id", toBase64(deviceMetadata_.deviceId));
context->AddMetadata("x-auth-token", accessToken.toStdString()); // plain text
context->set_deadline(std::chrono::system_clock::now() + std::chrono::seconds(30));
```

## Storage

- `SecureStorage` — AES-256/PBKDF2 шифрование токенов
- `AppSettings` — QSettings wrapper (незашифрованные настройки)
