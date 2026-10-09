# Barkfluff.AdminPanel

Веб-панель управления платформой. ASP.NET Minimal API на внутреннем HTTP-порту 51888; UI находится в Pages/v2/. Детали структуры: [[Backend/AdminPanel-ProjectMap]] и [[Backend/AdminPanel-Files]].

## Вход и роли

- Для bootstrap требуется ровно одна запись Telegram:Admins — неизменяемый Owner. Других администраторов добавляют по приглашению; роли и привязка к Telegram ID хранятся в LiteDB.
- Вход по имени администратора создаёт временный запрос (по умолчанию 10 минут). Telegram-бот адресует его выбранному активному администратору; после одобрения создаётся токен панели.
- API и защищённые страницы проверяют cookie auth_token. Токен в LiteDB продлевает LastActivity при использовании и истекает после 3 дней без активности (Auth:TokenExpirationDays). Login.html записывает cookie из JavaScript на 7 дней, поэтому cookie не HttpOnly; срок серверного токена короче.
- Viewer — пустой базовый набор ролей. Support управляет чтением/сессиями/паролями пользователей и почтой; ContentAdmin — контентом, ботами, зарезервированными именами, S3 просмотром и уведомлениями; OperationsAdmin — Docker, удалёнными серверами, конфигурацией и удалением Seq; SecurityAdmin — 2FA, federation, админами, аудитом и соответствующими пользовательскими действиями. Owner разрешено всё. Источник матрицы: Models/AdminPermissions.cs.
- Опасные операции дополнительно требуют step-up через Telegram. Подтверждение связано с токеном сессии, ключом действия и хэшем параметров; одноразовое, ожидание и окно после одобрения — по 5 минут. Состояние хранится в памяти процесса.

## Управление сервисом

Endpoint-группы в Program.cs охватывают auth/step-up/admins/audit, Docker и удалённые серверы, конфигурацию, Seq/логи/метрики, пользователей/чаты/файлы, badges/stickers, bots/federation, reserved names, notifications, mail и S3. Точные маршруты и permission checks — в Endpoint-файлах.

Панель опрашивает liveness/readiness, Docker, Seq и инфраструктуру для своей страницы health; собственный HTTP endpoint — /api/health/overview. Это отдельный сборщик, не общий readiness из [[Backend/GrpcServer]]. `HealthCollectorService` опрашивает каждые 30 секунд. `MetricsCollectorService` строит часовые rollups из Seq каждые 5 минут: общая статистика хранится 24 часа, ServiceMetrics — 30 дней; восстановление пробелов идёт до 72 часов, по 6 часов за цикл.

Nightly compose запускает панель от root и монтирует /var/run/docker.sock, данные /app/db, compose-файл и .env. Поэтому панель имеет доступ к Docker host. В исходнике cookie создаётся клиентским JS; не предполагайте HttpOnly.

Сборка: dotnet build Backend/Barkfluff.AdminPanel/Barkfluff.AdminPanel.csproj. Конфигурация по умолчанию — Backend/Barkfluff.AdminPanel/appsettings.json.
