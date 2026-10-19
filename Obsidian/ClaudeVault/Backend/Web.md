# BarkFluff.Web

ASP.NET Core-хост браузерного клиента и gRPC-Web-шлюз. Режим задаётся `Web:Mode`. Пути внутри компонента ниже отсчитываются от `Backend/BarkFluff.Web/`; `Shared/` и `Docker/` — от корня репозитория.

## Режимы

- **Node** раздаёт клиент, проксирует gRPC-Web к сервисам ноды и предоставляет HTTP-маршруты загрузки Files. `/ping/{service}` проксирует анонимный `/ping` соответствующего сервиса.
- **Shell** раздаёт выбор ноды и проксирует только API Navigator. Он не публикует gRPC-маршруты сервисов ноды.
- **Proxy** направляет запросы к настроенному Node-шлюзу и релеирует `/media/{host}/...` только для хостов из allowlist.

`/ping/navigator` использует отдельный внутренний `NavigatorService:HealthHost`, поскольку публичный адрес Navigator предназначен для gRPC. `Proxy` передаёт `/ping/{service}` на Node; сам Shell маршрутов ping сервисов не создаёт.

## Шлюз и клиент

YARP перенаправляет gRPC-Web к внутренним gRPC-сервисам. Для потоков Updates, Onliner, FastAuth и Calls задан таймаут активности 24 часа; для остальных кластеров — 100 секунд. `GrpcWebTrailersMiddleware` сохраняет gRPC trailers для клиента. `/node-config.js` сообщает клиенту режим выбора ноды, а `/pwa-config.js` отдаёт публичную конфигурацию PWA.

`wwwroot/messenger.html` загружает модули из `wwwroot/js/app/`; основные области — авторизация, API и соединение, сообщения и realtime, файлы, presence и звонки, папки и закреплённые сообщения, профиль, настройки, поиск и PWA. Proto-бандл браузера находится в `wwwroot/js/proto/`; после изменений proto его генерируют `scripts/generate-proto.*`. Контракты задаёт `Shared/BarkFluff.Proto`.

## Развёртывание

`Dockerfile.slim` собирает образ Web. Внешний Proxy-режим задаётся в `Docker/nightly/proxy/docker-compose.yml`, а его ingress — в `Docker/nightly/proxy/nginx/sites/proxy.conf`; `Docker/nightly/nginx/sites/web.conf` относится к основному Node ingress. Shell — режим того же Web-хоста; сервисные адреса, CORS и публичная конфигурация берутся из appsettings, переменных среды и Settings там, где он подключён. См. также [[Backend/Navigator]], [[Backend/Files]], [[Backend/Calls]], [[Backend/Updates]].
