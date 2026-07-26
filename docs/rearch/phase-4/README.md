# Фаза 4 — Presence и typing — планы реализации

Детальные планы по каждому этапу Фазы 4 из [../10-roadmap.md](../10-roadmap.md). Каждый план самодостаточен: исполнитель должен суметь выполнить этап, прочитав только план + указанные в нём файлы, не восстанавливая контекст всей федерации.

**Суть фазы:** онлайн-статусы и «печатает…» пересекают границу ноды. Это принципиально другой транспортный профиль, чем у сообщений ([../07-presence-typing.md](../07-presence-typing.md)): **не через outbox**, а живыми S2S-стримами (presence) и fire-and-forget вызовами (typing); потеря события некритична, персистентность не нужна. После Фазы 4 пользователь ноды B видит, что его remote-собеседник в сети и печатает — теми же UI-механизмами, что и для локальных (клиентский рендер — Фаза 5).

## Предпосылки (проверить до старта)

- **Фаза 2 целиком** (2.1–2.9): `RemoteUsers` с `ServerName` (2.1), доставка событий и `ChatMembers.UserUuid`/`ServerName` в федеративных чатах (2.3), профильные события (2.9). Без 2.3 федеративных чатов нет — presence некому показывать.
- **Фаза 3** технически не блокирует этот код (файлы и presence не пересекаются), но по роадмапу идёт раньше — если 3 не закрыта, уточни у владельца, менять ли порядок.
- 0.4 — proto готово: `SubscribePresence`/`PresenceEvent`/`PresenceStatus`, `DeliverTyping`, сервис `OnlinerServerApi` (`UpsertRemoteStatus`, `InjectRemoteTyping`), параллельные `user_uuid`/`user_uuids`-поля в `onliner_api.proto`. Реализаций нет — все три отвечают `Unimplemented`.
- 1.3/1.4 — `S2SChannelFactory` (XFed + SPKI), `ServerResolver`; XFed-интерсептор покрывает server-streaming (стрим только в ответе, запрос унарный — подпись работает без доработок).

## Порядок выполнения

```
4.1 → 4.2 → 4.3 → 4.4     (строго последовательно: 4.1 даёт федеративный контекст
                           членства, 4.2 — uuid-ветку Onliner, на которую опираются
                           и presence-мост (4.3), и typing-мост (4.4))
```

| Этап | План | Что делает |
|------|------|-----------|
| 4.1 | [step-4.1-membership-federated.md](step-4.1-membership-federated.md) | Messages: федеративный контекст в `CheckChatMembership` (uuid-запросы, признак federated, ноды-участники) + `CheckFederatedPresenceAccess` |
| 4.2 | [step-4.2-onliner-uuid-branch.md](step-4.2-onliner-uuid-branch.md) | Onliner: uuid-ветка статусов (Redis), `OnlinerServerApi.UpsertRemoteStatus`/`InjectRemoteTyping`, подписки по uuid, эвикция, интерес-heartbeat |
| 4.3 | [step-4.3-federation-presence.md](step-4.3-federation-presence.md) | Federation: агрегированный `SubscribePresence` (обе стороны), privacy на origin, coalescing, reconnect, лимиты |
| 4.4 | [step-4.4-typing-bridge.md](step-4.4-typing-bridge.md) | Typing: `DeliverTypingOutbound` → S2S `DeliverTyping` → `InjectRemoteTyping`, coalescing и rate limit, capability-флаги |

**Гейт фазы** (из роадмапа): статус remote-пользователя виден локальному подписчику; обрыв стрима → статусы гаснут, реконнект восстанавливает; privacy origin-стороны соблюдён; «печатает…» видно через ноду, спам-частота режется.

## Обязательные правила для исполнителя

1. **Работать в текущей ветке** (`dev`), веток не создавать. После завершения каждого этапа — коммит (`git push` не делать). Формат сообщения: `feat(rearch-phase4): <этап> — <суть>`.
2. **Строго по плану.** Ничего сверх написанного: никаких клиентских UI (Фаза 5), никакой персистентности remote-статусов, никаких федеративных групп. Если план противоречит реальному коду — остановись и спроси, либо адаптируйся минимально и явно опиши отклонение в коммите.
3. **Обратная совместимость — жёсткое требование.** Локальные presence/typing работают ровно как сейчас (те же RPC, те же события, та же нагрузка). Всё федеративное включается только при `Federation:Enabled = true` и наличии remote-участников. Старые клиенты, не знающие `user_uuid`-полей, продолжают работать.
4. **Эфемерность — принцип фазы.** Ни один presence/typing-путь не пишет в PostgreSQL, не идёт через outbox и не ретраится. Потеря события допустима by design; вместо ретраев — периодическое обновление и реконнект.
5. **Референсные образцы, не строки.** Планы называют файлы-образцы — читай актуальное состояние образца и повторяй стиль; номера строк ориентировочные.
6. **Библиотеки — только по актуальной документации**: gRPC server-streaming (семантика deadline/cancellation, keepalive), MassTransit (fan-out endpoint'ы per-instance), StackExchange.Redis — сверяйся с Context7/официальными доками, не полагайся на память.
7. **Obsidian**: 4.1 → `Backend/Messages.md`; 4.2 → `Backend/Onliner.md` + `Backend/Onliner-Metrics.md`; 4.3/4.4 → `Backend/Federation.md`, `Backend/Onliner.md`, `Shared/Queue.md` (если менялись Queue-события). Это часть definition of done.
8. **Проверка каждого этапа** — раздел «Критерии готовности» в конце плана. Этап не завершён, пока все пункты не пройдены.
9. Контекст решений — [../07-presence-typing.md](../07-presence-typing.md) (главный документ фазы), [../04-federation-service.md](../04-federation-service.md); риски №42 (слежка через presence), №27 (privacy фильтрует владелец), И-7 (эвикция кеша remote-статусов) — [../09-problems-open-questions.md](../09-problems-open-questions.md) и [../11-plan-review.md](../11-plan-review.md).

## Что проверяет исполнитель, а что — разработчик

Правило репозитория: **Docker для верификации не поднимать** (`CLAUDE.md`). Поэтому:

- **Исполнитель (агент)** — юнит/интеграционные тесты без Docker (образец есть: `Tests/BarkFluff.Federation.Tests` гоняет реальные хосты на loopback-Kestrel и SQLite/EF-InMemory), сборка затронутых сервисов, статическая проверка.
- **Разработчик (человек)** — E2E на двух-нодовом стенде `Backend/dev-federation-testbed/` (стенду для этой фазы нужны onliner + users + messages на обеих нодах). Такие пункты помечены **[делает разработчик]**; исполнитель перечисляет их в отчёте как оставшиеся, не выдавая за выполненные.

## Решения, зафиксированные на уровне фазы

Приняты при планировании фазы после сверки с фактическим кодом; уточняют [../07-presence-typing.md](../07-presence-typing.md) (правки дока входят в соответствующие этапы):

- **Док 07 описывает устаревшую модель Onliner.** В нём presence — `ConcurrentDictionary` в памяти инстанса. Фактически (после работ по масштабированию, `docs/scaling/onliner.md`) presence живёт в **Redis** (sorted set `onliner:presence`, member = `long userId`), а доставка изменений между инстансами идёт через **RabbitMQ fan-out** (`OnlineStatusChangedEvent`, `TypingChangedEvent`, очереди per-instance, autodelete). Все решения фазы строятся на фактической модели; формулировки «параллельный ConcurrentDictionary по uuid» из 07 заменяются на Redis-ключи (правка 07 — этап 4.2).
- **Remote-статусы хранятся отдельным Redis-ключом на пользователя** (`onliner:presence:remote:{uuid}` со значением статуса и меткой, TTL), а **не** в `onliner:presence`. Причина: sorted set обслуживается `OfflineDetectionService`, который гасит «протухшие» записи через 5 секунд без heartbeat — для remote это неверно (источник истины на чужой ноде, heartbeat'ов у нас нет). TTL-ключ даёт эвикцию бесплатно и без фонового сервиса, закрывая И-7 из [../11-plan-review.md](../11-plan-review.md) (монотонный рост кеша).
- **Onliner не знает, на какой ноде живёт remote-uuid.** Список «интересующих» uuid Onliner отдаёт Federation **плоским списком**, а группировку по нодам делает Federation (у него есть Users/`RemoteUsers`). Это сохраняет изоляцию: Onliner остаётся сервисом статусов, не сервисом федерации.
- **Интерес к remote-presence передаётся heartbeat'ом, а не дельтами.** Onliner горизонтально масштабируется, стримы подписчиков живут на разных инстансах; дельты «+uuid/−uuid» от нескольких инстансов невозможно свести без общего состояния. Решение: каждый инстанс Onliner раз в N секунд шлёт Federation полный список uuid, за которыми следят **его** подписчики (`SetPresenceInterest(instance_id, uuids[])`, TTL ≈ 3N); Federation объединяет наборы живых инстансов. Рестарт инстанса/сервиса самолечится через TTL, ретраи не нужны.
- **Изменения статусов Federation берёт из RabbitMQ, а не отдельным стримом из Onliner.** `OnlineStatusChangedEvent` уже публикуется fan-out'ом для межинстансной доставки — Federation заводит свою per-instance очередь и слушает те же события. Долгоживущий внутренний gRPC-стрим Onliner → Federation не нужен вовсе. Начальный снимок статусов при открытии S2S-подписки берётся отдельным unary-RPC у Onliner.
- **Privacy фильтрует origin-сторона** (инвариант №27): `OnlineVisibility != All` ⇒ в S2S-стрим уходит `PRESENCE_STATUS_UNKNOWN`. Federation кеширует privacy пользователя на короткий TTL (иначе каждое изменение статуса = вызов Users); окно рассинхрона после смены настройки ограничено TTL и фиксируется в доке как принятый компромисс MVP.
- **Проверка отношений обязательна** (№42, С-2 ревью): origin отдаёт статус только по uuid, у которых есть активный федеративный чат с нодой-подписчиком. Проверка — новый RPC Messages `CheckFederatedPresenceAccess` (этап 4.1), не privacy-фильтр.
- **`CheckChatMembership` расширяется вместо заведения второго RPC**: тот же вызов начинает принимать `user_uuid` (для проверки remote-участника) и возвращать per-chat федеративный контекст (`is_federated`, ноды-участники). Он и так вызывается на каждый typing-heartbeat — федеративная маршрутизация получается бесплатно, без второго round-trip.
- **Новые internal-RPC Federation** (`SetPresenceInterest`, `DeliverTypingOutbound`) добавляются в `federation_internal_api.proto` в этой фазе — в контракте 0.4 их не было, добавление RPC обратно-совместимо.
- **Typing на приёме валидируется через Messages(B)**, а не «на доверии»: `sender_uuid` обязан быть участником чата и принадлежать origin-ноде (та же логика, что у импорта событий в 2.4).
