# Фаза 1 — Federation-сервис: каркас, доверие, discovery — планы реализации

Детальные планы по каждому этапу Фазы 1 из [../10-roadmap.md](../10-roadmap.md). Каждый план самодостаточен: исполнитель (Sonnet 5) должен суметь выполнить этап, прочитав только план + указанные в нём файлы, не восстанавливая контекст всей федерации.

**Суть фазы:** появляется сервис `BarkFluff.Federation` (порт 7030) с ключами, подписями S2S-трафика (XFed), реестром пиров (KnownServers) и полной discovery-цепочкой; Navigator получает персистентность и federation-поля; nginx и AdminPanel готовятся к эксплуатации. **Никакой репликации чатов** — доставка сообщений это Фаза 2. После Фазы 1 нода умеет находить другие ноды и обмениваться с ними подписанным `Ping`, не более.

## Предпосылки (проверить до старта)

Фаза 0 должна быть завершена целиком:

- 0.1 — выполнен (коммит `f8f792ad`): `ServiceId.Federation = 15`, дефолты конфигурации (`RunSettings`, `FederationDb`, `Federation:Enabled`, `FederationService:Host/Token`) уже в каталоге Settings.
- 0.4 — файлы `Shared/BarkFluff.Proto/federation_api.proto` и `federation_internal_api.proto` должны существовать. Если их нет — Фаза 0 не закончена, остановись.
- 0.5 — отчёт `../phase-0/step-0.5-report.md` с выбранной Ed25519-библиотекой должен существовать (нужен этапам 1.2/1.3). Если его нет — остановись.

## Порядок выполнения

```
1.1 → 1.2 → 1.3 → 1.4 → 1.6 → 1.7     (строго последовательно)
1.5                                    (независим после Фазы 0; выполнить ДО финальной
                                        проверки 1.4 — там нужен Navigator.GetServerByName)
```

| Этап | План | Что делает |
|------|------|-----------|
| 1.1 | [step-1.1-service-skeleton.md](step-1.1-service-skeleton.md) | Каркас `BarkFluff.Federation`: проект, Program.cs, БД, Dockerfile.slim, CI, docker-compose |
| 1.2 | [step-1.2-keys-wellknown.md](step-1.2-keys-wellknown.md) | Ed25519-ключи ноды, `GetServerKeys`, подписанный `/.well-known/barkfluff` |
| 1.3 | [step-1.3-xfed-signing.md](step-1.3-xfed-signing.md) | XFed: подпись исходящих, проверка входящих, SPKI-пиннинг, тестовый двух-нодовый стенд |
| 1.4 | [step-1.4-discovery-knownservers.md](step-1.4-discovery-knownservers.md) | Discovery-цепочка (well-known → Navigator → manual), анти-SSRF, блоклист, фоновый рефреш |
| 1.5 | [step-1.5-navigator-persistence.md](step-1.5-navigator-persistence.md) | Navigator: PostgreSQL, federation-поля, валидация регистрации, `GetServerByName` |
| 1.6 | [step-1.6-nginx.md](step-1.6-nginx.md) | Nginx: `federation.{domain}`, `/.well-known`, rate-limit; стенд через nginx с self-signed |
| 1.7 | [step-1.7-adminpanel-federation.md](step-1.7-adminpanel-federation.md) | AdminPanel: страница «Федерация» (пиры, ключи, блок) |

**Гейт фазы** (из роадмапа): двух-нодовый стенд, ноды видят друг друга всеми тремя способами discovery, весь S2S подписан и проверяется.

## Обязательные правила для исполнителя

1. **Работать в текущей ветке** (`dev`), веток не создавать. После завершения каждого этапа — коммит (`git push` не делать). Формат сообщения: `feat(rearch-phase1): <этап> — <суть>`.
2. **Строго по плану.** Ничего сверх написанного: никакого outbox, консюмеров RabbitMQ, импорта событий — это Фаза 2. Если план противоречит реальному коду (файл переехал, API изменился) — остановись и спроси, либо адаптируйся минимально и явно опиши отклонение в коммите.
3. **Обратная совместимость — жёсткое требование.** Существующие сервисы и клиенты не меняют поведения. Federation по умолчанию выключен (`Federation:Enabled = false`); при пустом `Federation:ServerName` сервис стартует, но S2S-функции честно отвечают ошибкой «федерация не настроена».
4. **Референсные образцы, не строки.** Планы называют файлы-образцы (Onliner, Bots, users.conf …) — всегда читай актуальное состояние образца и повторяй его стиль; номера строк в планах ориентировочные.
5. **Миграции EF Core**: известный баг — `dotnet ef migrations add` может падать с `MissingMethodException`. Порядок: сначала `dotnet tool restore && dotnet tool run dotnet-ef migrations add <Name> --project <csproj>`; если падает — писать миграцию вручную (три файла: миграция, `.Designer.cs` с атрибутом `[Migration]`, обновление snapshot; образец — соседние миграции). Миграции применяются на старте сервиса (`Database.Migrate()`), локальный прогон обязателен.
6. **Крипто и сеть — только по актуальной документации**: перед использованием Ed25519-библиотеки, gRPC-расширений (`IServiceMethodProvider`, marshaller'ы), nginx-директив — сверяйся с Context7/официальными доками, не полагайся на память.
7. **Obsidian**: этап 1.1 создаёт `Obsidian/ClaudeVault/Backend/Federation.md` (+ ссылка в `Index.md`); последующие этапы дополняют его; 1.5 обновляет `Backend/Navigator.md`, 1.7 — файлы AdminPanel. Это часть definition of done.
8. **Проверка каждого этапа** — раздел «Критерии готовности» в конце плана. Этап не завершён, пока все пункты не пройдены.
9. Контекст решений — родительские доки [../02-trust-and-certs.md](../02-trust-and-certs.md), [../03-discovery.md](../03-discovery.md), [../04-federation-service.md](../04-federation-service.md); каждый план ссылается на конкретные разделы. Реестр рисков — [../09-problems-open-questions.md](../09-problems-open-questions.md).

## Решения, зафиксированные на уровне фазы

Эти решения приняты при планировании фазы и уточняют родительские доки (правки доков включены в соответствующие этапы):

- **Хранение приватных ключей — в `FederationDb` (таблица `SigningKeys`)**, а не в Configuration-сервисе. Ключ не покидает Federation, ротация требует структурного хранилища в любом случае, обратный канал записи в Configuration не нужен. Этап 1.2 правит доки 02 и 09 (№33).
- **`GetServerKeys` — единственный неподписанный S2S-RPC** (bootstrap-канал получения ключей). Всё остальное, включая `Ping`, требует XFed-подписи.
- **Все S2S-RPC v1 имеют унарные запросы** (стримы только в ответах: `FetchFile`, `SubscribePresence`) → подпись wire-байтов запроса покрывает каждый RPC, спец-случая client-streaming в протоколе v1 нет. Этап 1.3 фиксирует уточнение в доке 02.
- **`RotateSigningKey`** добавляется в `federation_internal_api.proto` (в Фазе 0.4 его не было; добавление RPC обратно-совместимо).
- **Двух-нодовый стенд** живёт в `Backend/dev-federation-testbed/` (мини-стек второй ноды: postgres + configuration + federation; в 1.6 добавляется nginx с self-signed сертами).
- **Navigator остаётся вне платформенного шаблона** (без `LoadConfiguration` — это публичная инфраструктура вне ноды): БД задаётся через переменную окружения.
