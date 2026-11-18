# AdminPanel — карта проекта

Подробное поведение и ограничения: [[Backend/AdminPanel]]. Исходники: Backend/Barkfluff.AdminPanel/.

## Основные части

- Program.cs поднимает ASP.NET Minimal API на 51888, подключает TokenAuthMiddleware и маппит endpoint-группы.
- Data/: TokenDbContext хранит токены, администраторов и приглашения; отдельные контексты обслуживают аудит, кэш метрик и удалённый Docker.
- Models/: AdminRole/AdminPermissions задают RBAC; PendingAuthRequest и PendingStepUp — временные процессы входа и подтверждения.
- Services/: AuthService, TokenService, AdminService, AdminInvitationService, TelegramBotService и StepUpService реализуют доступ; прочие сервисы интегрируют Users, Identity, Files, Messages, Bots, Federation, Seq, S3, Docker и SMTP.
- Pages/v2/: веб-страницы текущей панели.

## Endpoint-группы

Program.cs регистрирует /api/auth, /api/step-up, /api/admins, /api/audit, /api/seq, /api/seq/export, /api/seq/clear, /api/seq/compress-metrics, /api/docker, /api/badges, /api/stickers, /api/users, /api/chats, /api/files, /api/bots, /api/federation, /api/configuration, /api/reserved-names, /api/remote, /api/notifications и /api/mail. Актуальные проверки доступа находятся в соответствующих Endpoints/*.cs и AdminPermissions.cs.

## Подтверждённые ограничения

- Telegram:Admins задаёт ровно одного неизменяемого Owner; записи остальных администраторов и их изменяемые роли хранятся в LiteDB.
- Запросы входа и step-up живут в памяти процесса. Step-up однократный и привязан к session token, action key и хэшу параметров.
- Docker socket подключён в nightly compose; операции панели имеют доступ к Docker host.
- Новые endpoint-группы могут добавляться в Program.cs; не следует трактовать этот список как весь API-контракт.
