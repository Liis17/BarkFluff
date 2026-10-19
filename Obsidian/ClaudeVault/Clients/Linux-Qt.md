# BarkFluffQt

Parent: [[Index]]

Qt-клиент в `Linux/`. Источники ниже указаны относительно `Linux/`; UI-ориентир — [[Clients/DesignDocument]], сетевые контракты — [[Shared/Proto]].

## Стек и сборочная граница

`CMakeLists.txt`: CMake ≥3.20, C++20, Qt 6 Core/Widgets/Network/Svg/Concurrent/Multimedia/MultimediaWidgets/DBus, gRPC/Protobuf и OpenSSL. KF6 Config/CoreAddons подключаются опционально. Цель — `BarkFluffQt`, protobuf генерируется при сборке.

В текущем `CMakeLists.txt` `PROTO_DIR` жёстко задан как `${CMAKE_SOURCE_DIR}/../BarkFluffBackend/Shared/BarkFluff.Proto`, а такого каталога в этом дереве нет: канонические proto лежат в `Shared/BarkFluff.Proto/` от корня репозитория. Поэтому прежняя инструкция «cmake .. → make» не является подтверждённой рабочей сборкой текущего monorepo без исправления/обеспечения ожидаемого layout. Скрипт зависимостей — `install_deps.sh`; он предназначен для Debian/Ubuntu.

## Основные потоки

| Область | Точки входа и контракт |
|---------|------------------------|
| Запуск/нода | `src/main.cpp`, `src/UI/MainWindow.cpp`, `src/UI/ServerSelectPage.cpp`; Navigator выбирает ноду, Beacon получает `GetServerInfo` и per-service endpoints |
| Auth | `src/Connection/IdentityClient.cpp`: Auth/FastAuth, CreateToken, регистрация/подтверждение, reset/set password, OTP и сессии; это прежний RPC-поток, не новый клиент challenge controller |
| Messenger | `src/UI/MessengerPage.cpp`, `src/Models/Chat.cpp`, Connection Messages/Users/Files; сообщения/вложения/профили, pending-сообщения с отрицательным локальным ID |
| Realtime | Connection Updates/Onliner, Qt signals/slots; streaming context без обычного deadline |
| Настройки | `src/UI/Settings/`: General/Security/Sessions/Storage/About; PIN UI — `src/UI/PinUnlockDialog.cpp` |
| Медиа/уведомления | `src/Services/FileCacheService.cpp`, `NotificationService.cpp`; D-Bus freedesktop Notifications и tray fallback |

## Transport

`src/Connection/GrpcClient.cpp` создаёт TLS channel с системным CA bundle и встроенным Let's Encrypt R12 intermediate; `skipTlsVerify` сохранён для совместимости, но не отключает TLS verification. `tls=false` создаёт insecure channel. Unary context имеет deadline 30 секунд; `createContextNoDeadline` предназначен для длительного server streaming. Лимиты gRPC send/receive — 64 МБ.

Device metadata (`x-device-id`, `x-device-name`, `x-os-name`, `x-ip-address`, `x-app-name`, `x-app-version`) кодируются UTF-8/Base64; `x-auth-token` передаётся открытой строкой внутри transport. Beacon отвечает конфигурацией, а не heartbeat-механизмом. Межплатформенный стек не означает реализованную parity по Calls/Federation/E2E; проверять конкретные Connection/UI пути.

## Сессия и storage

`src/Services/SessionManager.cpp` — singleton. До сохранения токенов требуется `initializeWithPin`; restore извлекает user ID/username из JWT, повторно получает конфигурацию через Beacon и может вызвать CreateToken. Refresh-поток сохраняет новый access и прежний refresh token; совместимость с rotation нужно проверять отдельно по Identity. `clearSession` очищает storage и AppSettings.

`src/Storage/SecureStorage.cpp` хранит `secure.dat` в ConfigLocation/BarkFluff: AES-256-CBC, PBKDF2-HMAC-SHA256 (100000 iterations), случайные salt/IV по 16 байт; PIN остаётся в памяти для операций storage. Это собственный файловый контейнер, не интеграция libsecret/keyring. `AppSettings` — незашифрованный QSettings wrapper. Не описывать secure storage как универсальную гарантию, не подтверждённую реализацией.

`NotificationService` подавляет собственные сообщения и сообщения открытого активного чата; preview/avatar зависят от настройки. D-Bus actions «открыть»/«прочитано» поднимают Qt signals. Текущие инварианты и схемы проверять по источникам; `SESSION_PERSISTENCE.md` — вспомогательная документация, не замена коду.
