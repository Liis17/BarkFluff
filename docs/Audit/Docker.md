# Аудит: Docker-сборки и compose
> Дата: 2026-06-12. Область: docker-compose-dev/master, sample.env, все Dockerfile/Dockerfile.slim.

## Сводка

Docker-инфраструктура в целом построена грамотно: все 17 сервисов используют multi-stage сборку с кэшированием NuGet, финальные образы — chiseled (non-root по умолчанию), RabbitMQ и Redis не опубликованы наружу, privileged/cap_add нигде не используются, CI дополнительно тегирует образы по SHA. Главные проблемы концентрируются вокруг **admin-panel** (root + docker.sock = полный контроль над хостом при компрометации веб-панели) и **публикации портов на 0.0.0.0 в master-компоузе** — PostgreSQL, MinIO, Seq и все plaintext-gRPC-порты сервисов доступны со всех интерфейсов хоста, хотя nginx проксирует на них через 127.0.0.1. Также: слабые дефолтные пароли в sample.env, прокачка всего `.env` (включая SSH-пароли root и приватный ключ Firebase) в контейнер configuration, полное отсутствие healthcheck'ов, лимитов ресурсов и ротации логов в compose.

| Критичность | Безопасность | Производительность/сборка | Всего |
|-------------|--------------|---------------------------|-------|
| Critical    | 2            | 0                         | 2     |
| High        | 6            | 0                         | 6     |
| Medium      | 3            | 4                         | 7     |
| Low         | 1            | 3                         | 4     |
| **Итого**   | **12**       | **7**                     | **19** |

---

## Безопасность

### S1. Admin-panel: root + /var/run/docker.sock = root-эквивалент хоста — Critical
**Файл:** `Backend/docker-compose-master.yml:146` (`user: root`), `:178` (docker.sock); `Backend/docker-compose-dev.yml:184`, `:228`
**Проблема:** Контейнер admin-panel запускается под root (`user: root` в compose переопределяет `USER $APP_UID` из Dockerfile) и монтирует `/var/run/docker.sock`. Дополнительно в Dockerfile (`Backend/Barkfluff.AdminPanel/Dockerfile:14,17-19`) ставится docker-cli и docker-cli-compose.
**Почему это проблема:** Доступ к docker.sock эквивалентен root на хосте: любой RCE/инъекция в веб-панель (которая принимает внешний трафик через nginx) позволяет запустить привилегированный контейнер с примонтированным `/` хоста и захватить сервер целиком — включая все БД, секреты и остальные сервисы. `user: root` усугубляет: даже без docker.sock процесс в контейнере имеет uid 0. Группа `docker`, создаваемая в Dockerfile (строки 18-19), бесполезна — её GID внутри контейнера не совпадает с GID владельца сокета на хосте, из-за чего и появился костыль `user: root`.
**Рекомендация:** Убрать `user: root`; вместо этого передать GID хостовой группы docker через `group_add: ["<gid>"]`. Ещё лучше — не монтировать сокет напрямую, а использовать прокси с фильтрацией API (например, `tecnativa/docker-socket-proxy`), разрешив только нужные эндпоинты (containers list/restart), без `POST /containers/create` и `/exec`.

### S2. PostgreSQL опубликован на 0.0.0.0 — Critical (master) / High (dev)
**Файл:** `Backend/docker-compose-master.yml:283-284`; `Backend/docker-compose-dev.yml:334-335`
**Проблема:** `ports: "${POSTGRES_PORT}:${POSTGRES_PORT}"` → 5432 публикуется на всех интерфейсах хоста в обоих компоузах.
**Почему это проблема:** Прод-хост (master) интернет-facing — БД со всеми пользователями, сообщениями и сессиями доступна снаружи напрямую, защищена только паролем (который по sample.env может остаться `password`, см. S6). Docker сам прописывает iptables-правила и **обходит ufw/firewalld** — привычный хостовой firewall не спасёт. БД нужна только контейнерам в `barkfluff-network` (доступ по имени `postgres`) и, возможно, админу с localhost.
**Рекомендация:** В master убрать публикацию порта совсем (сервисы ходят по внутренней сети) или привязать к loopback: `"127.0.0.1:${POSTGRES_PORT}:5432"`. В dev — как минимум `127.0.0.1:`.

### S3. Все gRPC-порты сервисов опубликованы на 0.0.0.0 в master — High
**Файл:** `Backend/docker-compose-master.yml:12` (beacon), `:24` (configuration), `:40-42` (files), `:53` (identity), `:64` (messages), `:75` (notification), `:86` (users), `:97` (fast-auth), `:109` (updates), `:120` (onliner)
**Проблема:** Все микросервисы публикуют свои plaintext-HTTP/2 (gRPC) порты 7000–7015 на всех интерфейсах. При этом nginx на хосте проксирует на них через loopback (`Backend/nginx/barkfluff.single-server.conf:2,6,10` — upstream'ы `127.0.0.1:*`).
**Почему это проблема:** Внешний клиент может обойти nginx (TLS-терминацию, rate-limiting, маршрутизацию) и обращаться к сервисам напрямую по нешифрованному каналу. Межсервисные токены и трафик уязвимы; configuration-сервис (раздаёт конфигурацию с секретами другим сервисам) тоже доступен снаружи. Docker-публикация обходит ufw (см. S2).
**Рекомендация:** Привязать все публикации к loopback: `"127.0.0.1:${PORT}:${PORT}"` — для nginx на том же хосте этого достаточно. Порты, которые нужны только другим контейнерам, не публиковать вообще.

### S4. MinIO (S3 API + Console) опубликован наружу — High (master) / Medium (dev)
**Файл:** `Backend/docker-compose-master.yml:208-210`; `Backend/docker-compose-dev.yml:258-260`
**Проблема:** Публикуются и S3 API (9000), и веб-консоль администратора (9001) на всех интерфейсах.
**Почему это проблема:** Консоль MinIO с root-учёткой (по sample.env — `user`/`password`, см. S6) наружу — это полный доступ ко всем бакетам (файлы пользователей, badge-images). Сервисам MinIO нужен только внутри сети (`http://minio:9000`).
**Рекомендация:** Убрать публикацию обоих портов в master (или `127.0.0.1:`, если консоль нужна админу через SSH-туннель). В проде, по памяти проекта, S3 — это HostKey, так что локальный MinIO наружу не нужен тем более.

### S5. Seq опубликован наружу + слабый first-run пароль — High (master) / Medium (dev)
**Файл:** `Backend/docker-compose-master.yml:193-194,197`; `Backend/docker-compose-dev.yml:244-245,248`; `Backend/sample.env:52`
**Проблема:** Веб-интерфейс Seq (8880→80) публикуется на 0.0.0.0; admin-пароль задаётся `SEQ_FIRSTRUN_ADMINPASSWORD` из `.env`, где дефолт в шаблоне — `password`. Переменная действует только при первом запуске — если volume `seq_data` создан до того, как пароль сменили, слабый пароль остаётся навсегда.
**Почему это проблема:** Seq агрегирует логи всех сервисов — там потенциально содержатся PII, токены, строки подключения и метрики (через ServiceMetrics). Доступ к нему снаружи со слабым паролем = утечка всего, что когда-либо попало в логи.
**Рекомендация:** Привязать к `127.0.0.1:` (admin-panel ходит в Seq по внутренней сети `http://seq:80`, наружный порт нужен только людям) и убедиться, что admin-пароль реально сменён в работающем инстансе, а не только в `.env`.

### S6. Слабые дефолтные креденшелы в sample.env — High
**Файл:** `Backend/sample.env:12-13` (MinIO `user`/`password`), `:16-17` (RabbitMQ `user`/`password`), `:20-21` (Postgres `user`/`password`), `:27-28` (Configuration DB `user`/`password`), `:52` (Seq `password`)
**Проблема:** Шаблон `.env`, который копируется в прод (master-compose читает те же переменные), содержит тривиальные пары `user`/`password` для всех инфраструктурных сервисов.
**Почему это проблема:** sample.env — единственный документированный источник переменных; «скопировал, поменял хосты, забыл пароли» — типичный сценарий. В сочетании с публикацией Postgres/MinIO/Seq на 0.0.0.0 (S2, S4, S5) это даёт удалённый вход с первого подбора. Compose не падает при пустых переменных, ничего не заставляет их сменить.
**Рекомендация:** Заменить дефолты на явные плейсхолдеры вида `CHANGE_ME__GENERATE_WITH_openssl_rand` (как уже сделано для `CHARLIEHOSTNAME`), добавить в шаблон комментарий-предупреждение и/или скрипт проверки на дефолтные значения перед `docker-compose up`.

### S7. env_file: .env прокачивает ВСЕ секреты в контейнер configuration — High
**Файл:** `Backend/docker-compose-master.yml:25-26`; `Backend/docker-compose-dev.yml:26-27`
**Проблема:** Сервис configuration получает через `env_file: .env` весь файл окружения целиком: токен Telegram-бота, SSH-пароли root от Navigator/MSK-серверов, приватный ключ Firebase, пароли всех почтовых ящиков, пароли MinIO/RabbitMQ/Seq — хотя самому сервису нужны только `CONFIGURATION_*` (которые и так заданы явно в `environment:` строками ниже).
**Почему это проблема:** Нарушение принципа наименьших привилегий: компрометация одного сервиса (или просто `docker inspect configuration` / чтение `/proc/1/environ` любым, у кого есть доступ к docker) раскрывает секреты всей платформы и **других физических серверов** (SSH root). Блок `environment:` с явным перечислением уже существует — `env_file` избыточен.
**Рекомендация:** Удалить `env_file: .env` из сервиса configuration в обоих компоузах — явный блок `environment:` покрывает всё необходимое.

### S8. SSH-доступ root по паролю к другим серверам через env-переменные — High
**Файл:** `Backend/sample.env:76-85`; `Backend/docker-compose-dev.yml:202-209`
**Проблема:** Admin-panel получает реквизиты SSH к Navigator- и MSK-серверам: пользователь по умолчанию `root`, аутентификация по паролю, всё хранится в `.env` и в env контейнера.
**Почему это проблема:** Компрометация admin-panel (см. S1) автоматически даёт root на двух других физических серверах. Парольная SSH-аутентификация под root — наихудшая комбинация: перехватывается, брутфорсится, не отзывается гранулярно.
**Рекомендация:** Перейти на выделенного непривилегированного пользователя с ключевой аутентификацией (ключ — через секрет/volume, не env) и sudo только на нужные команды; на серверах выключить `PermitRootLogin` и `PasswordAuthentication`.

### S9. Непиннованные образы `:latest` для инфраструктуры и сервисов — Medium
**Файл:** `Backend/docker-compose-master.yml:190` (seq), `:221` (rabbitmq), `:234` (redis), `:10,22,38,...` (все barkfluff-образы `:latest`); `Backend/docker-compose-dev.yml:241,271,284` и все сервисы
**Проблема:** `datalust/seq:latest`, `rabbitmq:latest`, `redis:latest` и все сервисные образы `barkfluff-*:latest` без фиксации версии. Пиннованы только `postgres:18` (по мажору) и MinIO (полный релиз — хорошо).
**Почему это проблема:** `docker-compose pull` в произвольный момент молча обновит RabbitMQ/Redis/Seq на новую мажорную версию — возможны breaking changes и несовместимость данных в volume (особенно RabbitMQ и его mnesia/khepri-хранилище). Для своих сервисов `:latest` лишает воспроизводимости и отката: CI уже тегирует по SHA (`.github/workflows/build-backend-*.yml`), но деплой эти теги не использует.
**Рекомендация:** Зафиксировать инфраструктуру минимум по мажору (`rabbitmq:4`, `redis:8`, `datalust/seq:2024`), обновлять осознанно. Для сервисов в master деплоить по SHA-тегу (через переменную `TAG` в compose).

### S10. Web Dockerfile: загрузка бинарей без проверки контрольных сумм и npm без lock-файла — Medium
**Файл:** `Backend/BarkFluff.Web/Dockerfile:11-20,24-25`; `Backend/BarkFluff.Web/Dockerfile.slim:13-22,26-27`
**Проблема:** В proto-stage скачиваются `protoc` и `protoc-gen-grpc-web` с GitHub через curl без верификации checksum/signature; `npm install --omit=dev` выполняется по `package.json` без `package-lock.json` (в `Backend/BarkFluff.Web/scripts/` lock-файла нет).
**Почему это проблема:** Подмена релизного бинаря или зависимостей npm (supply chain) попадёт прямо в JS-бандл, который исполняется в браузерах всех пользователей веб-клиента. Без lock-файла сборка невоспроизводима: завтра `npm install` может притянуть другие версии транзитивных зависимостей.
**Рекомендация:** Добавить `package-lock.json` в репозиторий и использовать `npm ci --omit=dev`; для protoc/protoc-gen-grpc-web — проверять SHA256 после скачивания (`echo "<sha>  file" | sha256sum -c`).

### S11. Секреты как env-переменные и монтирование .env в admin-panel — Medium
**Файл:** `Backend/docker-compose-master.yml:181` (`./.env:/.env:ro`), `:180` (compose-файл в контейнер); `Backend/docker-compose-dev.yml:230-231`, `:133-137` (Firebase private key через env)
**Проблема:** Полный `.env` со всеми секретами платформы монтируется в admin-panel; приватный ключ Firebase в dev передаётся через переменную окружения (в master — корректнее, файлом `:135`). Секреты в env видны через `docker inspect`, `/proc/*/environ` и наследуются дочерними процессами.
**Почему это проблема:** Расширяет поверхность утечки: каждый, кто может читать контейнер admin-panel (а это веб-приложение наружу), читает секреты всех сервисов. На фоне S1 это вторично, но после устранения S1 останется самостоятельной дырой.
**Рекомендация:** Использовать Docker secrets / отдельные файлы с минимальным набором для каждого сервиса; admin-panel — выдать отдельный токен-набор вместо сырого `.env`. Firebase-ключ в dev передавать файлом, как в master.

### S12. Единая плоская сеть без сегментации — Low
**Файл:** `Backend/docker-compose-master.yml:289-291`; `Backend/docker-compose-dev.yml:339-341`
**Проблема:** Все контейнеры — от внешних (web, admin-panel) до БД/брокера/кэша — находятся в одной bridge-сети `barkfluff-network`.
**Почему это проблема:** Любой скомпрометированный сервис имеет L4-доступ к Postgres, Redis (без пароля — `redis-server --appendonly yes`, строка 240 master / 290 dev), RabbitMQ и MinIO. Сегментация (frontend/backend/data-сети) ограничила бы латеральное перемещение.
**Рекомендация:** Минимум — вынести postgres/redis/rabbitmq/minio в отдельную сеть `data`, подключив к ней только сервисы, которым она нужна. Redis — задать `requirepass`.

---

## Производительность / качество сборки

### P1. Полное отсутствие healthcheck'ов в compose, depends_on без условий — Medium
**Файл:** `Backend/docker-compose-master.yml` (все сервисы, напр. `:18-19,47-48`); `Backend/docker-compose-dev.yml` (аналогично); единственный HEALTHCHECK — `Backend/BarkFluff.Web/Dockerfile:76-77`
**Проблема:** Ни один сервис в обоих компоузах не имеет `healthcheck:`; все `depends_on` — короткая форма (только порядок старта). HEALTHCHECK есть только в Dockerfile веб-клиента.
**Почему это проблема:** `restart: always` перезапускает только упавший процесс — зависший контейнер (deadlock, отвал gRPC) будет числиться `Up` бесконечно. `depends_on` без `condition: service_healthy` означает, что сервисы стартуют до готовности configuration/postgres и полагаются на внутренние ретраи. Это удлиняет холодный старт и порождает шум ошибок в Seq.
**Рекомендация:** Добавить healthcheck'и хотя бы инфраструктуре (`pg_isready`, `rabbitmq-diagnostics ping`, `redis-cli ping`, `mc ready local` / curl для MinIO) и перевести `depends_on` на `condition: service_healthy`. Для chiseled-образов сервисов (нет shell) — либо gRPC health probe снаружи, либо встроенный dotnet-healthcheck-исполняемый файл.

### P2. Нет лимитов ресурсов ни у одного контейнера — Medium
**Файл:** `Backend/docker-compose-master.yml` (весь файл), `Backend/docker-compose-dev.yml` (весь файл)
**Проблема:** Ни `mem_limit`/`cpus`, ни `deploy.resources` не заданы. При этом Postgres явно затюнен «под 2 ядра / 8 ГБ, БД делит машину с остальными сервисами» (`docker-compose-master.yml:247-248`) — то есть на одном слабом хосте живут ~15 контейнеров.
**Почему это проблема:** Один протёкший .NET-сервис (или всплеск нагрузки на messages/updates) съест всю память — OOM-киллер хоста начнёт убивать произвольные процессы, включая Postgres (риск восстановления WAL, простой всей платформы). Без `cpus` один контейнер может затормозить все остальные.
**Рекомендация:** Задать каждому сервису `mem_limit` (например, 256–512m для .NET-сервисов, 1.5–2g для postgres с учётом shared_buffers=768MB) и suммарно вписаться в 8 ГБ с запасом под ядро/кэш. Учесть, что .NET в контейнере с лимитом сам корректно подстраивает GC.

### P3. Логи Docker без ротации — Medium
**Файл:** `Backend/docker-compose-master.yml` (нет ключа `logging:` ни у одного сервиса), `Backend/docker-compose-dev.yml` (аналогично)
**Проблема:** Драйвер логирования не настроен — используется дефолтный `json-file` без `max-size`/`max-file` (если не переопределён в daemon.json хоста, чего в репозитории не видно).
**Почему это проблема:** ~15 болтливых сервисов (gRPC + Serilog в stdout) на хосте с одним диском — `/var/lib/docker/containers/*-json.log` растут неограниченно до заполнения диска. Заполненный диск уронит Postgres и Seq.
**Рекомендация:** Добавить общий якорь в compose: `logging: { driver: json-file, options: { max-size: "20m", max-file: "3" } }` или задать `log-opts` глобально в `/etc/docker/daemon.json` на хостах. Учитывая, что логи и так идут в Seq, локальные json-логи можно резать агрессивно.

### P4. Раздутый build-context: .dockerignore не исключает клиентов и Rust target (1.3 ГБ) — Medium
**Файл:** `.dockerignore:1-25` (корень репозитория); контекст сборки — корень репо: `.github/workflows/build-backend-*.yml` (`context: .`) и все `Backend/*/Dockerfile`
**Проблема:** Контекст всех сборок — весь репозиторий. `.dockerignore` исключает bin/obj/node_modules/.git, но не исключает: `**/target` (Rust-артефакты `Backend/BarkFluff.Users.Rust/target` — **1.3 ГБ** на машине разработчика), а также ненужные бэкенду каталоги `Android/` (203 МБ), `Frontend/` (129 МБ), `Windows/` (76 МБ), `Linux/`, `Mac/`, `iOS/`, `Obsidian/`, `docs/`, `Tests/`. Даже после исключений контекст ≈ 254 МБ, при том что slim-сборкам в CI нужен только `publish/` (+ для Web — `Shared/BarkFluff.Proto` и `scripts/`).
**Почему это проблема:** Контекст целиком передаётся docker-демону при каждой сборке каждого из 17 сервисов: локальная сборка отправляет 1.5+ ГБ, CI — сотни мегабайт впустую; это время, диск и инвалидация кэша.
**Рекомендация:** Дополнить `.dockerignore`: `**/target`, `Android/`, `Frontend/`, `Windows/`, `Linux/`, `Mac/`, `iOS/`, `Obsidian/`, `docs/`, `Tests/`, `*.md`. Это безопасно: полные Dockerfile'ы используют только `Backend/*` и `Shared/*`, slim — только `publish/` и proto.

### P5. `COPY . .` копирует весь репозиторий в build-stage — Low
**Файл:** Все полные Dockerfile, напр. `Backend/BarkFluff.Beacon/Dockerfile:12`, `Backend/BarkFluff.Files/Dockerfile:14`, `Backend/BarkFluff.Web/Dockerfile:62` (затронуты все 17 сервисов)
**Проблема:** После корректного слоя restore (csproj отдельно — паттерн соблюдён, это плюс) выполняется `COPY . .` всего контекста вместо копирования только `Backend/<Сервис>`, `Backend/BarkFluff.GrpcServer` и `Shared/`.
**Почему это проблема:** Любое изменение любого файла в репозитории (документация, другой сервис) инвалидирует слой `COPY . .` и форсирует полный `dotnet publish`. В CI это смягчено slim-файлами (publish на раннере), но локальные/ручные сборки полного Dockerfile пересобираются всегда. Частично лечится P4.
**Рекомендация:** Либо ограничиться расширением `.dockerignore` (P4), либо заменить `COPY . .` на адресные `COPY Backend/<Svc> ...`, `COPY Backend/BarkFluff.GrpcServer ...`, `COPY Shared/ Shared/`.

### P6. Устаревший атрибут `version: '3.8'` в compose — Low
**Файл:** `Backend/docker-compose-dev.yml:1`; `Backend/docker-compose-master.yml:1`
**Проблема:** Поле `version` устарело в Compose Specification — современный `docker compose` его игнорирует и печатает предупреждение `the attribute 'version' is obsolete`.
**Почему это проблема:** Не вредит, но шумит в выводе каждого запуска и вводит в заблуждение относительно используемого формата.
**Рекомендация:** Удалить первую строку из обоих файлов.

### P7. AdminPanel Dockerfile без кэш-маунта NuGet — Low
**Файл:** `Backend/Barkfluff.AdminPanel/Dockerfile:8,11`
**Проблема:** Единственный из 17 сервисов, где `dotnet restore`/`dotnet publish` выполняются без `--mount=type=cache,target=/root/.nuget/packages` (во всех остальных Dockerfile кэш есть).
**Почему это проблема:** Каждая полная сборка admin-panel заново скачивает все NuGet-пакеты — медленнее и сильнее нагружает сеть, чем у остальных сервисов; непоследовательность паттерна.
**Рекомендация:** Добавить тот же `RUN --mount=type=cache,...`, что и в остальных Dockerfile.

---

## Положительные наблюдения (вне находок)

- Все полные Dockerfile — multi-stage, слой restore отделён от исходников (правильный порядок слоёв), используются BuildKit cache mounts.
- Финальные образы 15 из 17 сервисов — `aspnet:10.0-noble-chiseled` (минимальная поверхность, non-root 1654 по умолчанию, `USER $APP_UID` задан явно); Web и AdminPanel на alpine также с `USER $APP_UID` в Dockerfile (для AdminPanel перекрыт compose'ом — см. S1).
- RabbitMQ и Redis не публикуют порты наружу (явно закомментировано) — в отличие от Postgres/MinIO/Seq.
- `privileged`, `cap_add`, host network — нигде не используются.
- `restart: always` задан у всех контейнеров в обоих компоузах.
- MinIO пиннован на конкретный релиз; CI тегирует образы по SHA коммита (хоть деплой их пока не использует — см. S9).
