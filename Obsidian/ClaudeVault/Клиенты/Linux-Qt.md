# BarkFluffQt (Linux)

Кроссплатформенный клиент на Qt 6 / C++20 с gRPC.

Расположение: `Linux/`

## Tech Stack

| Компонент | Технология |
|-----------|------------|
| GUI Framework | Qt 6 (Widgets, Network, Svg, Concurrent, Multimedia) |
| Language | C++20 |
| Build System | CMake 3.20+ |
| RPC | gRPC + Protobuf |
| Encryption | OpenSSL |
| Optional | KDE Frameworks 6 (KConfig) |

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
│   ├── Pages/      # MainWindow, LoginPage, MessengerPage, ...
│   └── Widgets/    # Переиспользуемые виджеты
├── Services/       # SessionManager, FileCacheService
├── Storage/        # SecureStorage, AppSettings
└── Utils/          # ErrorHandler, Validators, MessageGrouper, ...
```

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

- **Singleton**: SessionManager, FileCacheService
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
