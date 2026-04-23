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
- Auth flow через `IdentityApi.Auth` по JSON-over-gRPC-Web (`Content-Type: application/json`, `Connect-Protocol-Version: 1`)
- Токен JWT хранится в `AuthContext`, передаётся во все API-вызовы через заголовок `x-auth-token`

### API клиент (`src/api/client.ts`)

- gRPC-Web вызовы через `@connectrpc/connect-web` (fetch-based transport)
- `buildHeaders(token)` — формирует все XAuth заголовки: `x-auth-token` + device metadata (base64)
- Прокси в dev: Vite proxy `/grpc` → `http://localhost:7020`

### Структура компонентов

```
src/
├── App.tsx              # AuthContext, роутинг
├── main.tsx             # Entry point
├── api/
│   └── client.ts        # gRPC-Web клиент + API-функции
├── auth/
│   └── LoginPage.tsx    # Форма авторизации + 2FA
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

Конфигурация: `buf.gen.yaml`. Генерирует TypeScript-клиенты из `.proto` файлов.

```bash
npx buf generate
```

Proto-файлы скопированы в `Frontend/Developers/proto/`.

## Деплой

`npm run build` → `dist/` монтируется к существующему nginx-контейнеру.

## Связанные файлы

- [[Backend/Developers]] — бэкенд API (gRPC-Web)
- [[Shared/Proto]] — proto контракты
- [[Архитектура]] — XAuth, device headers
