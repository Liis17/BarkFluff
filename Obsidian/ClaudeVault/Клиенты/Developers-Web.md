# Developers-Web

React-фронтенд портала документации BarkFluff для разработчиков клиентских приложений.

Расположение: `Frontend/Developers/`

## Tech Stack

| Технология | Версия |
|------------|--------|
| React | 19 |
| TypeScript | 5.x |
| Vite | 6.x |
| @connectrpc/connect | 1.x |
| @connectrpc/connect-web | 1.x |
| @bufbuild/protobuf | 1.x |

## Сборка

```bash
cd Frontend/Developers
npm install
npm run build     # → dist/
npm run dev       # → http://localhost:5173
```

## Архитектура

### Авторизация

- `LoginPage` — форма логина + 2FA (OTP)
- Auth flow через `IdentityApi.Auth` с транспортом `createGrpcWebTransport` (`Content-Type: application/grpc-web+proto`)
- Типизированный клиент: `createClient(IdentityApi, identityTransport)` из `src/gen/identity_api_connect`
- OTP ошибка детектируется через `ConnectError.metadata.get('x-error-code')`
- Токен JWT хранится в `AuthContext` + персистится в `localStorage` под ключом **`barkfluff_dev_auth`** (JSON с `accessToken`/`refreshToken`/`accessTokenExpiration`/`refreshTokenExpiration`). При старте App.tsx проверяет expiration и автоматически очищает просроченные токены.
- `deviceId` (UUID) генерируется один раз и сохраняется в `localStorage` под ключом **`barkfluff_device_id`** — используется для `x-device-id` header в gRPC-метаданных.
- Передаётся во все API-вызовы через заголовок `x-auth-token` (plaintext, без base64)

### API клиент (`src/api/client.ts`)

- gRPC-Web вызовы через `createClient(DevelopersApi, developerTransport)` (typed, `@connectrpc/connect-web`)
- `buildHeaders(token)` — формирует XAuth заголовки: `x-auth-token` + device metadata (base64); без `Content-Type`/`Connect-Protocol-Version`
- Прокси в dev: Vite proxy `/grpc` → `http://localhost:7020`

### Структура компонентов

```
src/
├── App.tsx              # AuthContext, роутинг
├── main.tsx             # Entry point
├── api/
│   └── client.ts        # gRPC-Web клиент + API-функции
├── auth/
│   ├── LoginPage.tsx    # Форма авторизации + 2FA
│   └── tokenManager.ts  # get/save/clear токенов в localStorage (ключ barkfluff_dev_auth) — фактическая логика хранения токенов (не в App.tsx)
├── components/
│   ├── DocsPage.tsx     # Главная страница документации
│   ├── Layout/
│   │   ├── Header.tsx   # Навигация + logout
│   │   └── Sidebar.tsx  # Collapsible nav groups
│   └── Sections/
│       ├── Overview.tsx
│       ├── Quickstart.tsx
│       ├── Implementation.tsx
│       ├── AuthHeaders.tsx
│       ├── ConnectionFlow.tsx
│       ├── ErrorCodes.tsx
│       └── ProtoFile.tsx   # Proto viewer с подсветкой
└── styles/
    └── global.css       # Полный CSS (Manrope + JetBrains Mono)
```

### Секции документации

| Секция | Содержание |
|--------|-----------|
| Overview | Обзор платформы |
| Quickstart | Быстрый старт для новых разработчиков |
| Implementation | Паттерны реализации |
| AuthHeaders | XAuth заголовки (x-auth-token, device metadata, base64) |
| ConnectionFlow | Схема подключения через Beacon |
| ErrorCodes | Все коды ошибок из Shared.Exceptions |
| ProtoFile | Просмотр .proto файлов с синтаксической подсветкой |

## Proto-генерация

Конфигурация: `buf.gen.yaml` + `buf.yaml`. Генерирует TypeScript-клиенты из `.proto` файлов.

```bash
npm run generate   # = buf generate
```

Proto-файлы скопированы в `Frontend/Developers/proto/`.
Сгенерированные файлы: `src/gen/` (identity_api_pb.ts, identity_api_connect.ts, developers_api_pb.ts, developers_api_connect.ts).

## Деплой

`npm run build` → `dist/` монтируется к существующему nginx-контейнеру.

## Связанные файлы

- [[Backend/Developers]] — бэкенд API (gRPC-Web)
- [[Shared/Proto]] — proto контракты
- [[Архитектура]] — XAuth, device headers
