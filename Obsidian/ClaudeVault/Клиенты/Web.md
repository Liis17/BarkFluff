# Web

Веб-клиент мессенджера BarkFluff — полноценное **React 19 + TypeScript SPA** на Material 3 Expressive. Заменил старый vanilla-JS клиент (`Backend/BarkFluff.Web/wwwroot/*.html` + `js/app/*.js`).

Расположение: `Frontend/Web/`
Раздаётся сервисом [[Backend/BarkFluff.Web]] (YARP-прокси gRPC-Web↔gRPC) из его `wwwroot/` после `npm run build`.

## Tech Stack

| Технология | Версия |
|------------|--------|
| React | 19 |
| TypeScript | 5.7 |
| Vite | 6.x |
| react-router-dom | 7.x |
| zustand | 5.x (состояние чатов/темы) |
| @connectrpc/connect(-web) | 1.x |
| @bufbuild/protobuf | 1.x |

## Сборка

```bash
cd Frontend/Web
npm install
npm run generate              # buf generate → src/gen (из proto/)
npm run sync-proto            # обновить proto/ из Shared/BarkFluff.Proto
npm run build                 # tsc + vite → ../../Backend/BarkFluff.Web/wwwroot
npm run dev                   # http://localhost:517x, проксирует gRPC на BF_PROXY (по умолч. :7016)
```

Сборка пишет в `wwwroot` c `emptyOutDir:false` — старый `messenger.html` и `js/` сохраняются как fallback на `/messenger`.

## Архитектура

### Транспорт и авторизация
- `baseUrl: '/'` для `createGrpcWebTransport` — connect-web формирует `/{pkg}.{Service}/{Method}`, что совпадает с YARP-маршрутами прокси.
- `api/transport.ts` — клиенты с auth-интерсептором (`api/interceptor.ts`): подставляет device-метаданные + `x-auth-token`, при `UNAUTHENTICATED` делает forced refresh + ретрай.
- `api/baseTransport.ts` — «голый» транспорт (без интерсептора) для login/refresh/pre-auth/стримов — разрывает цикл импортов.
- `api/tokenStore.ts` — токены в localStorage/sessionStorage (порт старого `tokens.js`); `api/refresh.ts` — single-flight refresh; `api/metadata.ts` — base64-заголовки.
- Коды ошибок: `api/errorCodes.ts` (OTP_REQUIRED / INVALID_OTP / INVALID_CREDENTIALS / INVALID_USERNAME_FORMAT).
- Состояние авторизации: `state/AuthContext.tsx` (login/logout/applySession, currentUserId из JWT).

### Real-time
- `realtime/RealtimeProvider.tsx` — 7 server-streaming подписок [[Backend/Updates|UpdatesApi]] (new/read/edited/deleted/pinned/unpinned/all-unpinned).
- `realtime/streams.ts` — переподключение с backoff 2с→30с, forced refresh при auth-ошибке, resync активного чата при переоткрытии (стримы не реплеят пропущенное), реакция на `visibilitychange`.
- События пишутся напрямую в `state/chatStore.ts` (Zustand) → UI обновляется реактивно.

### Роутинг
`/login`, `/register`, `/chats`, `/chats/:chatId`, `/settings/{profile,sessions,security,storage}`. `RequireAuth`/`PublicOnly` (`app/RequireAuth.tsx`). Layout — `app/AppLayout.tsx` + `components/NavRail`.

### Функции (MVP)
- **Логин + 2FA** (OTP-шаг), **регистрация** (9-шаговый мастер `features/register/RegisterWizard.tsx`).
- **Чаты**: список ([[Backend/Messages|MessagesApi]].ListChats), просмотр сообщений с пагинацией вверх, разделители дат.
- **Сообщения**: отправка/редактирование/удаление/закреп/прочитано, вложения (загрузка REST `/api/files/upload`, скачивание через [[Backend/Files|FilesApi]].GetTempDownloadUrl).
- **Настройки**: профиль ([[Backend/Users|UsersApi]] Change*/SetProfilePicture), сессии (GetActiveSessions/RemoveActiveSession/RenameDevice), 2FA (Enable/Confirm/Disable), хранилище (GetUserStorageInfo).

### Тема Material 3 Expressive
- `theme/tokens.css` — структурные токены (shape/motion/typescale), `theme/themes.css` — 3 темы (light/dark/midnight) с цветами 1:1 из старого `messenger.html` + цветовые роли `--md-sys-color-*`.
- `theme/ThemeProvider.tsx` + `state/themeStore.ts` — переключение, персист в localStorage, `data-theme` на `<html>`.
- Кастомные компоненты (`components/`): Button, IconButton, TextField, Avatar, Switch, NavRail.

## Не входит в MVP (задел на будущее)
Папки чатов, персонализация (фоны/постеры), QR fast-auth, E2E private-чаты (Argon2id+AES-GCM), секретные Signal-чаты. Архитектура (отдельный transport, стор) позволяет добавить позже.

## Связи
- [[Backend/BarkFluff.Web]] — хост-сервис (YARP + gRPC-Web конвертация), серверную часть не меняли.
- [[Архитектура]] — общий tech stack, XAuth, gRPC-клиент.
- [[Клиенты/Developers-Web]] — родственный React-проект (портал документации), использован как референс.
