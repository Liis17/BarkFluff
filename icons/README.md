# BarkFluff — иконки микросервисов

Векторные SVG-иконки для backend-микросервисов платформы. Первый проход: стиль зафиксирован, символика — по одной иконке на сервис.

## Стиль

- **Формат**: SVG, `viewBox="0 0 24 24"`, без встроенных цветов — только `currentColor` (наследует цвет из CSS/родителя).
- **Duotone на одном цвете**:
  - фон — плитка `rect x="2" y="2" width="20" height="20" rx="6"` с `fill="currentColor" fill-opacity="0.12"`;
  - глиф — линии `stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" fill="none"`, отдельные акцентные точки/детали — сплошная заливка `fill="currentColor"`.
- **Сетка глифа**: рабочая область 4–20 (безопасное поле 6–18), чтобы иконки были визуально одного размера рядом друг с другом.
- Цвет не задаётся внутри файла — тема (светлая/тёмная, акцентный цвет конкретного сервиса) применяется снаружи через CSS.

## Структура

```
icons/
  services/
    <service>.svg
```

Все иконки микросервисов — в `services/`, имя файла (kebab/lowercase) = имя сервиса.

## Сервисы и символика

| Файл | Сервис | Идея глифа |
|---|---|---|
| `configuration.svg` | Configuration — централизованная конфигурация | ползунки настроек |
| `beacon.svg` | Beacon — точка входа клиентов | маяк с сигнальными дугами |
| `navigator.svg` | Navigator — реестр серверов | компас со стрелкой |
| `identity.svg` | Identity — auth, JWT, 2FA, сессии | щит с галочкой |
| `users.svg` | Users — профили, устройства, бейджи | силуэт + значок-бейдж |
| `messages.svg` | Messages — чаты, сообщения, вложения | пузырь чата с точками |
| `files.svg` | Files — файлы, S3, стикеры | папка |
| `updates.svg` | Updates — real-time стриминг событий | круговые стрелки обновления |
| `onliner.svg` | Onliner — онлайн-статусы | точка присутствия с кольцами |
| `notification.svg` | Notification — email-уведомления | колокольчик |
| `fastauth.svg` | FastAuth — QR-авторизация устройств | QR-рамка с молнией |
| `adminpanel.svg` | AdminPanel — веб-дашборд администратора | столбчатая диаграмма |
| `cloudmessaging.svg` | CloudMessaging — push-уведомления (Firebase) | облако со стрелкой push |
| `web.svg` | Web — gRPC-Web прокси + статика | глобус |
| `webserver.svg` | WebServer — публичный HTTP-сервер | серверная стойка |
| `clientstorage.svg` | ClientStorage — хранилище клиентских приложений | архивная коробка |
| `developers.svg` | Developers — портал документации | код-скобки `</>` |
| `calls.svg` | Calls — звонки (аудио/видео) | телефонная трубка |
| `bots.svg` | Bots — Bot API | голова робота |
| `federation.svg` | Federation — межсерверная федерация (S2S) | связанные узлы-серверы |

Не включены: `GrpcServer` (shared-библиотека, не самостоятельный сервис), `Users.Rust` (экспериментальный drop-in порт Users, та же роль) и `Nginx` (инфраструктурный reverse proxy, не сервис приложения).
