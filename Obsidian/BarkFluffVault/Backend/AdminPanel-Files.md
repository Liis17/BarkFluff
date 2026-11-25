# AdminPanel — карта файлов

Обзор основных точек входа: [[Backend/AdminPanel]] · классы и API: [[Backend/AdminPanel-ProjectMap]]. Исходный каталог: Backend/Barkfluff.AdminPanel/.

## Структура

- Program.cs — DI, middleware, внутренний listener и регистрация endpoint-групп.
- Endpoints/ — HTTP API по предметным областям; маршруты перечислены в [[Backend/AdminPanel-ProjectMap]].
- Middleware/TokenAuthMiddleware.cs — проверка cookie auth_token для API и страниц.
- Models/ — сессии, администраторы, RBAC, step-up, аудит и DTO.
- Services/ — Telegram login и подтверждения, LiteDB доступ, внешние gRPC-клиенты, Docker/Compose, конфигурация, Seq, S3, почта и метрики.
- Data/ — отдельные LiteDB контексты для токенов и администраторов, метрик, удалённого Docker, аудита и истории задач деплоя.
- Pages/v2/ — текущий веб-интерфейс; Login.html реализует вход и установку cookie, assets/ содержит общий JS/CSS.
- Иконки интерфейса, включая favicon, берутся из корневого пакета Icons/ и публикуются по /assets/icons/. Статические SVG подключаются через CSS-маску, динамические — через bfIcon и bfSetIcon из assets/icons.js.
- appsettings.json — значения по умолчанию; секреты и адреса служб обычно передаются окружением.
- Dockerfile.slim — runtime image. В nightly compose к нему смонтированы Docker socket, compose-файл и данные панели.
