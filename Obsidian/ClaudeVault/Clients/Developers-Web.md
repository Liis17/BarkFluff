# Developers-Web

Parent: [[Index]]

Портал разработчиков — отдельный React/TypeScript frontend в `Frontend/Developers/`, не браузерный мессенджер [[Clients/Web]]. Источники ниже указаны относительно этого каталога.

## Сборка и wire snapshots

`package.json` задаёт React 19, TypeScript ~5.7, Vite ^6, Connect/Connect-web ^1.6 и protobuf ^1.10; это диапазоны manifest, точные installed версии определяет lockfile.

```bash
cd Frontend/Developers
npm ci
npm run build
npm run sync-proto:check
```

Build выполняет sync → buf generate → tsc → vite и создаёт `dist/`. `scripts/sync-public-proto.mjs` копирует `developers_api.proto`, `identity_api.proto`, `shared.proto` из канонического `Shared/BarkFluff.Proto/`; check-only сообщает drift без записи. Конфигурация генерации — `buf.yaml`/`buf.gen.yaml`, generated TypeScript — `src/gen/`. Dev proxy `/grpc` → `http://localhost:7020` задаёт `vite.config.ts`.

## Авторизация и API

- `src/App.tsx` содержит AuthContext и вызывает IdentityApi.Auth; `src/auth/LoginPage.tsx` показывает login/OTP. Запросы идут через `createGrpcWebTransport` с baseUrl `/grpc`, OTP-needed определяется по `ConnectError.metadata['x-error-code']`.
- JWT/device headers формируют App и `src/api/client.ts`: токен — `x-auth-token` без Base64, device metadata — Base64; app name `BarkFluff Developers Portal`, техническая версия `1.0.0`. `x-ip-address` не отправляется; IP доверенного proxy определяет backend.
- `src/auth/tokenManager.ts` валидирует JSON/expiration и хранит токены в `localStorage['barkfluff_dev_auth']`, удаляя повреждённую запись. `barkfluff_device_id` хранит один UUID устройства. Это localStorage, не encrypted storage.
- Refresh с запасом 30 секунд сериализуется одной in-flight promise. Текущий код сохраняет новый access token, оставляя прежний refresh; ошибка refresh очищает auth даже при transport failure. Не переносить сюда гарантию transient-retry/refresh rotation другого клиента.
- `src/api/client.ts` создаёт DevelopersApi client с authInterceptor: sections, proto list/content и error codes. `AUTH_CHANGED_EVENT` синхронизирует AuthContext с refresh/logout.

## UI и deployment

`src/components/DocsPage.tsx` запрашивает три каталога через `Promise.allSettled`: частичный отказ не скрывает успешно полученные данные. Proto content загружается лениво у viewport. `Sections/sectionData.tsx` валидирует JSON; `ErrorBoundary` даёт fallback при ошибке render. Основные секции — overview, quickstart, implementation, auth headers, connection flow, error codes и proto viewer.

Header/Login используют `public/favicon.ico`, исходный asset — `Backend/Barkfluff.WebServer/files/favicon.ico`; это отдельная копия. Сервисный `Backend/Barkfluff.Developers/Dockerfile.slim` собирает frontend и копирует dist в `/app/wwwroot`. Backend раздаёт SPA на HTTP/1 7021 и API на HTTP/2 7020; внешний Nginx разделяет `/grpc/...` и статику. Источник этой конфигурации — [[Backend/Developers]], общий auth — [[Shared/Auth]] и [[Architecture]].
