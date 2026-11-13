# BFT Proxy

Parent: [[Architecture]]. Прокси терминирует BFTv1 на Exit и маршрутизирует отдельные мультиплексированные потоки в HTTPS upstream; Relay пересылает зашифрованный транспорт без расшифровки. Wire protocol описан в `Docs/BFTv1.md`.

## Exit

- Основной код: `Backend/BarkFluff.BftProxy/BftProxyHost.cs`, `BftConnectionTransport.cs`, `BftForwarder.cs`.
- Каждый BFT-туннель несёт независимые соединения HTTP/1.1 или HTTP/2; один HTTP/2 канал может содержать несколько одновременных gRPC вызовов. Ошибка upstream завершает только соответствующий HTTP запрос/стрим, не сбрасывая BFT-туннель.
- Kestrel принимает синтетические соединения из `BftConnectionTransport`; request/response minimum data rates отключены. TCP keepalive включён на принятых сокетах.
- Ограничения установленных туннелей проверяются после BFT handshake. Превышение отправляет `GOAWAY(OVERLOAD)` и логирует метку ключа. Ключи перечитываются раз в 2 с; активный туннель отзывается, если его пара «метка, значение ключа» отсутствует в новом файле.
- Upstream connection pool живёт не более 10 минут. `X-BFT-Client-IP` не содержит IPv6 scope id. Proxy сохраняет статус и заголовки ответа, кроме `Alt-Svc`, удаляемого, чтобы клиент не обходил туннель через HTTP/3.

| Переменная | По умолчанию | Ограничение |
|---|---:|---|
| `BFT_MAX_TUNNELS_PER_KEY` | 16 | 1–4096 установленных туннелей на метку ключа |
| `BFT_MAX_TUNNELS` | 4096 | 1–65536 установленных туннелей всего |

Pending handshake limits независимы от лимитов установленных туннелей: 16 на IP и 1024 всего.

## Relay

- Источник: `Backend/BarkFluff.BftProxy/BftRelayHost.cs`.
- После EOF в одном направлении второе может работать ещё до 30 с по `TimeProvider`; истечение закрывает оба сокета и освобождает connection lease. Ошибки pump закрывают соединение сразу.
- TCP keepalive включён на клиентском и next-hop сокетах. Лимиты Relay действуют на активные подключения и включают IP и общий предел.

Интеграционные сценарии Exit и Relay находятся в `Tests/BarkFluff.BftProxy.Tests/`; тесты используют реальные loopback TCP/TLS/gRPC соединения и `ManualTimeProvider` для relay drain timeout.
