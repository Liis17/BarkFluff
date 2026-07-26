# Этап 1.6 — Nginx: federation-субдомен, /.well-known, rate-limit, стенд с TLS

## Цель

S2S-трафик ходит через nginx: субдомен `federation.{domain}` проксирует gRPC на Federation:7030, apex отдаёт `/.well-known/barkfluff`, включены rate-limit-зоны. Двух-нодовый стенд переводится на nginx с self-signed сертами — проверяется SPKI-пиннинг в бою.

## Контекст

- Требования к nginx: [../08-service-migration.md](../08-service-migration.md), раздел «Nginx» (таймауты длинных стримов, буферизация, CA-серт на apex для публичных нод).
- Существующие конфиги: `Backend/nginx/` — образец gRPC-субдомена `users.conf` (redirect 80→443, `listen 443 ssl http2`, `grpc_pass grpc://users:7001`, include `01-ssl-params.conf`, resolver docker DNS), общие файлы `00-default.conf`, `00-rate-limits.conf`, `01-ssl-params.conf`, референс одиночного сервера `barkfluff.single-server.conf`. **Прочитай их перед началом.**
- Well-known-документ отдаёт Federation по HTTP/1 на порту 7031 (этап 1.2).

## Изменение 1 — federation.conf

`Backend/nginx/federation.conf` по образцу `users.conf`:

- `server_name federation.<домен по образцу соседей>`; redirect 80→443; `listen 443 ssl http2`; include ssl-params; docker-resolver;
- `grpc_pass grpc://federation:7030` + стандартные `grpc_set_header` соседей;
- **отличие от соседей — долгоживущие стримы** (`SubscribePresence` в Фазе 4 живёт часами, `FetchFile` в Фазе 3 гоняет гигабайты): `grpc_read_timeout 3600s; grpc_send_timeout 3600s;` и отключение накопления ответа в прокси-буферах — сверь актуальные директивы буферизации для grpc-прокси по документации nginx (Context7/официальные доки), цель: чанки уходят клиенту сразу, стрим не рвётся по неактивности внутри окна таймаута;
- rate-limit (Изменение 3).

TLS-серт — серт ноды: для S2S допустим self-signed (чужие ноды проверяют SPKI-пин, а не CA) — пути сертов как в `01-ssl-params.conf` (оператор подставляет свои).

## Изменение 2 — /.well-known/barkfluff на apex

- В референс-конфиг `Backend/nginx/barkfluff.single-server.conf` (и в тот apex-конфиг, что реально используется на прод-домене — найди, какой server-блок обслуживает apex) добавить:

```nginx
location = /.well-known/barkfluff {
    proxy_pass http://federation:7031/.well-known/barkfluff;
    proxy_set_header Host $host;
}
```

- Комментарий в конфиге: на apex для публичных нод обязателен CA-валидный серт (Let's Encrypt) — это bootstrap-канал discovery ([../02-trust-and-certs.md](../02-trust-and-certs.md)); нода без него знакомится только через ручных пиров.

## Изменение 3 — rate-limit

В `00-rate-limits.conf` — новая зона по образцу существующих (per-IP, например `limit_req_zone ... zone=federation:10m rate=...`); подобрать rate по аналогии с самой нагруженной существующей зоной (S2S-батчи легитимно часты — не задуши; точный тюнинг — Фаза 6). Подключить `limit_req` в `federation.conf` и на location well-known (well-known можно жёстче — это редкие запросы).

## Изменение 4 — стенд через nginx + self-signed

Расширить `Backend/dev-federation-testbed/` (создан в 1.3):

1. Скрипт `make-certs.sh` (openssl): self-signed серт для каждой ноды + вывод SPKI-отпечатка (`openssl x509 -pubkey | openssl pkey -pubin -outform DER | openssl dgst -sha256 -binary | base64`).
2. nginx-контейнер перед federation каждой ноды (минимальный конфиг из Изменения 1, серты из скрипта).
3. `seed-peers.sql` обновить: endpoint'ы теперь `https://nginx-nodeX` + реальные SPKI-отпечатки в `TlsSpkiSha256`.
4. README стенда: полный сценарий — серты → up → сид → `fedping` через nginx.

## Чего НЕ делать

- Не менять существующие конфиги других сервисов (users.conf и т.д.).
- Не заводить прод-серты/домены — только референс + стенд; реальный прод-деплой nginx — руками оператора (эксплуатационная дока — Фаза 6.3).
- Не тюнить rate-limit под нагрузку (Фаза 6.2).

## Критерии готовности

1. Стенд: `fedping` node1→node2 через nginx с self-signed сертами — OK (SPKI-пин совпадает).
2. Негативный тест пиннинга: перегенерировать серт node2 **без** обновления сида у node1 → исходящий S2S от node1 отклоняется на TLS-этапе, метрика пиннинга растёт.
3. `curl https://<стендовый apex или nginx-node2>/.well-known/barkfluff -k` — документ отдаётся через nginx.
4. Rate-limit: цикл частых запросов на well-known location получает 429/503 (какой код у зоны — сверь с существующими).
5. `nginx -t` на полном наборе конфигов — успех.
6. Obsidian: `Backend/Federation.md` дополнен (nginx, порты, стенд с TLS); при необходимости пометка в `Архитектура.md` (новый субдомен в схеме портов/доменов).
7. Коммит: `feat(rearch-phase1): 1.6 — nginx federation-субдомен + well-known + rate-limit`.
