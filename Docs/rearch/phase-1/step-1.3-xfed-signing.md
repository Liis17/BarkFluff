# Этап 1.3 — XFed: подпись исходящих, проверка входящих, SPKI-пиннинг

## Цель

Весь S2S-трафик (кроме `GetServerKeys`) подписан Ed25519 и проверяется на приёме; S2S-клиент пиннит TLS-ключ пира. Появляется двух-нодовый тестовый стенд. Discovery ещё нет — ключи пиров на стенде сидируются вручную.

## Контекст

- Каноническая строка, заголовки, порядок проверок получателя: [../02-trust-and-certs.md](../02-trust-and-certs.md), «Подпись каждого S2S-запроса». Ключевое: `request-bytes` = **полученные wire-байты**, пере-сериализация на проверке запрещена (решение №36 в [../09-problems-open-questions.md](../09-problems-open-questions.md)).
- Библиотека — из отчёта [../phase-0/step-0.5-report.md](../phase-0/step-0.5-report.md); обёртки Sign/Verify уже есть (этап 1.2).
- Схема таблиц KnownServers: [../03-discovery.md](../03-discovery.md).

**Уточнение, зафиксированное фазой**: все S2S-RPC v1 имеют унарные *запросы* (стримы только в ответах — `FetchFile`, `SubscribePresence`), поэтому подпись wire-байтов запроса покрывает каждый RPC; случая client-streaming в протоколе v1 нет. В рамках этапа поправь [../02-trust-and-certs.md](../02-trust-and-certs.md): фразу про «подпись покрывает заголовки + первое сообщение стрима» замени этим уточнением (одно-два предложения, хирургически).

## Изменение 1 — таблицы KnownServers / KnownServerKeys

Миграция по схеме из 03 (поля как в доке: `ServerName` PK, `FederationEndpoint`, `TlsSpkiSha256 text[]`, `Source`, `Status`, `FirstSeenAt`, `LastSeenAt`, `LastKeyRefreshAt`, `ProtocolVersion`; дочерняя `KnownServerKeys`: `ServerName+KeyId` PK, `PublicKey`, `ExpiredAt`, `RevokedAt`). В 1.3 таблицы **только читаются** (ключи пиров для проверки подписей) и наполняются вручную (SQL-сид на стенде); наполнение кодом — 1.4.

## Изменение 2 — заголовки и каноническая строка

Константы (по образцу `MetadataKeys` в `Shared/BarkFluff.Shared.Auth`): `x-bf-origin`, `x-bf-destination`, `x-bf-timestamp`, `x-bf-key-id`, `x-bf-signature`. Каноническая строка:

```
{origin}\n{destination}\n{timestamp}\n{grpc-method-full-name}\n{hex(sha256(request-bytes))}
```

`grpc-method-full-name` — вида `/barkfluff.federation.FederationS2SApi/Ping`. Кодировка hash — зафиксируй hex lowercase (войдёт в спецификацию протокола). Вынеси построение строки в одну функцию, используемую и клиентом, и сервером.

## Изменение 3 — проверка входящих (сервер)

Задача — получить **сырые принятые байты** запроса до десериализации. В Grpc.AspNetCore штатный интерсептор видит уже распарсенное сообщение — байты нужно перехватывать ниже. Рабочий подход: кастомная привязка методов через `IServiceMethodProvider<T>` / `ServiceBinderBase` с обёрнутым `Marshaller`, который сохраняет принятый `byte[]` (например, в `HttpContext.Items`) и затем парсит штатно. Прежде чем писать — изучи актуальный API `Grpc.AspNetCore` через Context7; если найдёшь более простой поддерживаемый способ получить те же гарантии (хеш именно принятых байтов) — используй его и опиши отклонение в коммите.

Порядок проверок (из 02, всё в одном месте — «XFed-обработчик»):

1. Метод — `GetServerKeys`? → пропустить без проверки (bootstrap; whitelist по полному имени метода).
2. Все пять заголовков присутствуют, иначе `Unauthenticated`.
3. `destination == Federation:ServerName` (пустой ServerName → федерация не сконфигурирована → `FailedPrecondition`).
4. `|now - timestamp| <= Federation:SignatureWindowSeconds` (новый конфиг-ключ, дефолт 300 в коде). Нарушение → `Unauthenticated` + код ошибки `ClockSkewDetected` через существующий механизм `x-error-code` (заведи исключение по образцу `Shared/BarkFluff.Shared.Exceptions`) и серверное время в сообщении.
5. Ключ `(origin, key_id)` из `KnownServerKeys`, не отозван/не протух. Нет ключа → `Unauthenticated` (discovery-на-лету добавит 1.4).
6. `Verify(pub, canonical_string, signature)`. Провал → `Unauthenticated`, метрика `s2s_signature_failures`, warning-лог с origin.
7. `KnownServers[origin].Status == Blocked` → `PermissionDenied` (блоклист работает уже сейчас, наполняется в 1.4/1.7).

Успех → origin кладётся в контекст запроса (последующие этапы будут проверять `sender.server_name == origin`). XFed применяется **только** к `FederationS2SApi`; `FederationInternalApi` остаётся под XAuth.

## Изменение 4 — подпись исходящих (клиент)

Client-interceptor (образец структуры — `JwtClientInterceptor`): сериализует сообщение `IMessage.ToByteArray()` (тот же кодовый путь Google.Protobuf, что и у wire-marshaller'а — байты идентичны), строит каноническую строку, подписывает активным ключом (1.2), проставляет пять заголовков. `origin` = свой ServerName, `destination` = имя ноды-адресата (параметр фабрики канала).

## Изменение 5 — фабрика S2S-каналов + SPKI-пиннинг

`Services/S2SChannelFactory.cs`: `GetChannel(serverName)` → кеш `GrpcChannel` per-destination. Канал строится на `SocketsHttpHandler` с `RemoteCertificateValidationCallback`:

- вычислить `sha256(SubjectPublicKeyInfo)` предъявленного серта (base64), сравнить со списком `KnownServers.TlsSpkiSha256` этого пира; совпадение → принять (ошибки цепочки CA игнорируются — self-signed допустим); иначе → отклонить + метрика.
- Список пинов пуст (пир так и не опубликовал) → отклонять TLS-соединение (fail-closed), warning-лог. Plaintext-эндпоинты (`http://`) допускаются только на стенде — см. ниже.

Интерсептор подписи вешается на канал фабрики. Фабрика — единственный путь исходящих S2S (это пригодится 1.4 и Фазе 2).

## Изменение 6 — метрики

`s2s_requests_in`, `s2s_requests_out`, `s2s_signature_failures`, `s2s_clock_skew_rejections` — через `MetricsCollector` (реестр метрик — [../04-federation-service.md](../04-federation-service.md)).

## Изменение 7 — двух-нодовый стенд + тесты

1. **Юнит/интеграционные тесты** — новый проект `Backend/BarkFluff.Federation.Tests` (по образцу существующих тест-проектов бэкенда): каноническая строка (фиксированный вектор), подпись→проверка roundtrip, и через in-proc хост (`WebApplicationFactory` или два Kestrel в тесте): валидный Ping — OK; битая подпись — `Unauthenticated`; чужой `destination` — отказ; `timestamp` за окном — отказ с `ClockSkewDetected`; заблокированный origin — `PermissionDenied`.
2. **Стенд** `Backend/dev-federation-testbed/`: `docker-compose.node2.yml` — мини-стек второй ноды (postgres2 + configuration2 + federation2, свои volume/имена/порты, та же docker-сеть или своя с мостом), `seed-peers.sql` — вставка KnownServers/KnownServerKeys каждой ноды в БД другой (endpoint'ы plaintext `http://federation:7030` — TLS и пиннинг проверяются в 1.6 через nginx), `README.md` стенда: как поднять, как задать `Federation:ServerName` обеим нодам (записью в Configuration-БД каждой), как прогнать Ping.
3. Ручной прогон Ping между нодами: мини-CLI `Backend/dev-federation-testbed/fedping/` (консолька вне solution: адрес + origin/destination + seed-ключ из SQL → шлёт подписанный Ping, печатает ответ/ошибку).

## Чего НЕ делать

- Discovery, наполнение KnownServers кодом, UpsertManualPeer — 1.4.
- Nginx/серты — 1.6 (стенд пока plaintext).
- Подпись `FederationEvent.origin_signature` (per-event) — Фаза 2.

## Критерии готовности

1. Тесты `BarkFluff.Federation.Tests` зелёные (все случаи из Изменения 7.1).
2. Стенд: `fedping` node1→node2 и node2→node1 — OK; негативные случаи дают ожидаемые ошибки.
3. `GetServerKeys` по-прежнему работает без подписи; все остальные S2S-RPC без заголовков — `Unauthenticated`.
4. Метрики отказов видны в Seq после негативных прогонов.
5. Док 02 уточнён (унарные запросы, см. «Контекст»); Obsidian `Backend/Federation.md` дополнен (XFed, заголовки, стенд).
6. Коммит: `feat(rearch-phase1): 1.3 — XFed-подписи S2S + SPKI-пиннинг + стенд`.
