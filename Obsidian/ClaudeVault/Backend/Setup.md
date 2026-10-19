# BarkFluff.Setup

Временная web-консоль первичной настройки [[Backend/Settings]]. Не имеет своей БД и хранит сессии только в памяти процесса. Использует HTTP/1, default port 7032, Settings gRPC по SETTINGS_URL (default http://settings:7003). Код: Backend/BarkFluff.Setup/.

## Поток и защита

- Docker secret передаётся через SETUP_SECRET_FILE; при необходимости можно использовать SETUP_TOKEN. Сравнение setup token — constant-time.
- Успешный вход создаёт HttpOnly, SameSite=Strict cookie barkfluff_setup_session и CSRF token; session lifetime по умолчанию 2 часа и продлевается при обращении.
- Мутации требуют X-CSRF-Token. Если клиент прислал Origin, он должен совпадать с SETUP_PUBLIC_ORIGIN либо текущим origin.
- Ограничение входа — максимум 5 попыток на IP за 5 минут, а не только неудачных попыток.
- SetupEndpoints предоставляет /api/session, /api/setup/state, PUT /api/setup/groups/{groupId}, /api/setup/complete и DELETE /api/session. Completion проверяется Settings backend и после фиксации SetupState повторно не проходит.

Группы и их обязательность динамически приходят из Settings; UI повторно проверяет зависимости на сервере. Email может быть отключён без SMTP, Telegram требует bot token и node name, и должен оставаться хотя бы один канал регистрации.

## Deployment

Docker/nightly/barkfluff/docker-compose.setup.yml публикует SETUP_PORT (default 7032) только на 127.0.0.1, включает Settings setup mode и передаёт общий Docker secret в Setup и Settings. После настройки обычный Docker/nightly/barkfluff/docker-compose.yml выключает SETTINGS_SETUP_MODE.

Docker/setup/settings-setup.nginx.conf — шаблон внешнего HTTPS reverse proxy; перед установкой нужно задать домен/сертификат и проксировать на localhost:7032. Setup имеет собственный GET /health/live; общий readiness из GrpcServer не используется.

Сборка: dotnet build Backend/BarkFluff.Setup/BarkFluff.Setup.csproj.
