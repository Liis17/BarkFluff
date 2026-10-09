# AdminPanel — карта файлов

Обзор основных точек входа: [[Backend/AdminPanel]] · классы и API: [[Backend/AdminPanel-ProjectMap]]. Исходный каталог: Backend/Barkfluff.AdminPanel/.

## Структура

- Program.cs — DI, middleware, внутренний listener и регистрация endpoint-групп.
- Endpoints/ — HTTP API по предметным областям; маршруты перечислены в [[Backend/AdminPanel-ProjectMap]].
- Middleware/TokenAuthMiddleware.cs — проверка cookie auth_token для API и страниц.
- Models/ — сессии, администраторы, RBAC, step-up, аудит и DTO.
- Services/ — Telegram login и подтверждения, LiteDB доступ, внешние gRPC-клиенты, Docker/Compose, конфигурация, Seq, S3, почта и метрики.
- Data/ — отдельные LiteDB контексты для токенов и администраторов, метрик, удалённого Docker и аудита.
- Pages/v2/ — текущий веб-интерфейс; Login.html реализует вход и установку cookie, assets/ содержит общий JS/CSS.
- appsettings.json — значения по умолчанию; секреты и адреса служб обычно передаются окружением.
- Dockerfile.slim — runtime image. В nightly compose к нему смонтированы Docker socket, compose-файл и данные панели.
