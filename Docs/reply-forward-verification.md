# Разделение reply/forward — что осталось проверить

Задача: ответ и пересылка перестали быть одним и тем же на бэкенде. Реализовано полностью,
но часть проверок в среде разработки выполнить не удалось. Здесь — что уже проверено,
что нет и почему, и точные шаги для того, у кого среда полная.

Ветка: `claude/message-forward-reply-2qiobx`.
Коммиты: от `feat(messages): separate reply from forward in schema and contracts` до
`feat(winui): render replies from the server field, forward in batches`.

---

## Что проверено и зелёное

| Проверка | Результат |
|---|---|
| `dotnet build Backend/BarkFluff.Messages` | 0 ошибок |
| `dotnet build Backend/BarkFluff.Federation` | 0 ошибок |
| `dotnet test Tests/BarkFluff.Messages.Tests` | 420 passed (было 392) |
| `dotnet test Tests/BarkFluff.Federation.Tests` | 267 passed, 2 упавших — **предсуществующие**, воспроизводятся на чистом дереве (`XFedIntegrationTests`, DI не резолвит `FederationS2SApiHandler`) |
| `dotnet build` Client.Core / ClientV2.WPF / WebApi.Core | 0 ошибок (с `-p:EnableWindowsTargeting=true`) |
| `dotnet test Tests/BarkFluff.Client.WinUI.Tests` | 109 passed, 1 упавший — **предсуществующий** (`DpapiPrivateChatKeyStore`, DPAPI есть только под Windows) |
| Web: генерация proto-бандла штатным `scripts/generate-proto.sh` | собралось, новые поля в бандле есть |
| Web: `npm run lint` | новых предупреждений в затронутых файлах нет |
| Web: `npm run build:app` | собралось |
| Миграция `AddMessageReplyAndForwardMetadata` | сгенерирована штатным `dotnet ef`, компилируется |

---

## Что проверить не удалось

### 1. Сборка Android — блокирует политика сети

**Симптом.** `./gradlew :app-v1:assembleDebug` падает на конфигурации:

```
Plugin [id: 'com.google.gms.google-services', version: '4.4.4', apply: false] was not found
```

**Причина.** Артефакты Google Maven редиректят на `dl.google.com`, а прокси среды отвечает
403 на CONNECT к этому хосту. Плагин объявлен в корневом `Android/build.gradle.kts`, поэтому
падает даже `:core:assembleDebug` — обойти сборкой одного модуля нельзя. Обходить политику
сети я не стал.

**Что делать.** Собрать в нормальной среде:

```bash
cd Android
./gradlew :app-v1:assembleDebug
```

**На что смотреть в первую очередь** (эти места правились и не компилировались ни разу):

- `core/src/main/proto/shared.proto`, `messages_api.proto` — добавлены `ReplyInfo`,
  `Message.reply_to = 12`, поля 5–8 в `ForwardedMessageAttachment`,
  `OutgoingMessage.reply_to_message_id = 4` / `forwarded_message_ids = 5`.
  Проверить, что кодоген protobuf-lite отработал и выдал ожидаемые аксессоры.
- `MessageAdapter.kt` — `item.replyTo` типа `Shared.ReplyInfo`, доступ к `data.isDeleted`,
  `data.senderName`, `data.textPreview`, `data.firstAttachmentType`, `data.messageId`.
  Здесь наибольший риск: Java-protobuf для `bool is_deleted` генерирует `getIsDeleted()`,
  и я рассчитываю, что Kotlin увидит его как свойство `isDeleted`. Если нет — правится в одну строку.
- `MessageAdapter.kt` — `ViewMessageQuoteBinding.inflate(...)` в цикле по пересылкам и новый
  контейнер `binding.forwardQuotesContainer` (ViewBinding для него генерируется из изменённых
  `item_message_sent.xml` / `item_message_received.xml`, где `<include forwardQuote>` заменён
  на `LinearLayout`).
- `ChatActivity.kt` — `msg.hasReplyTo()`.

### 2. Сборка WinUI-приложения — XAML-компилятор только под Windows

**Симптом.**

```
error : XamlCompiler output file "obj/.../output.json" was not created. The XAML compiler may have crashed.
```

Общая библиотека `BarkFluff.Client.Core` собирается и покрыта тестами, а вот сам проект
`Windows/BarkFluff.Client.WinUI` — нет.

**Что делать.**

```powershell
dotnet build Windows/BarkFluff.Client.WinUI/BarkFluff.Client.WinUI.csproj
```

**На что смотреть.** `Views/MessengerPage.xaml`, блок цитаты (~строка 228). Там теперь два
элемента вместо одного: `Button` с `Visibility="{x:Bind HasReply, ...}"` и биндингами
`Reply.SenderName` / `Reply.Preview`, и `ItemsRepeater` по `Forwards` с
`DataTemplate x:DataType="vm:ForwardedContentViewModel"`. Риск — компиляция `x:Bind`
внутри `DataTemplate` и то, что `vm:` уже объявлен (`xmlns:vm="using:BarkFluff.Client.Core.ViewModels"`,
строка 7).

### 3. Тесты ClientV2.WPF не запускаются

Компилируются (`dotnet build` зелёный), но `dotnet test` падает: нет
`Microsoft.WindowsDesktop.App` под Linux. Запустить под Windows:

```powershell
dotnet test Tests/BarkFluff.ClientV2.WPF.Tests
```

ClientV2.WPF правился минимально и намеренно: он **не** ссылается на `Client.Core`
(своя копия ViewModel и сервиса), общий у него только `WebApi.Core`. Чтобы его поведение
осталось прежним, в `ForwardingLetter` сохранено устаревшее поле `ForwardedMessageId`,
которое маппится в устаревшее же поле proto. То есть ClientV2.WPF должен вести себя
**ровно как до задачи** — если это не так, это регрессия.

### 4. Живой прогон и БД

Правило репозитория запрещает поднимать Docker для верификации, поэтому не проверялись:

- применение миграции на реальном PostgreSQL;
- сквозной сценарий «отправил ответ → второй клиент увидел цитату»;
- федеративный сценарий между двумя нодами.

Ниже — что именно стоит прогнать руками.

---

## Сценарии для ручной проверки

### Ответы

1. Ответить на сообщение → у отправителя цитата появляется сразу, без перезагрузки истории.
2. **Прокрутить историю так, чтобы оригинал ушёл из загруженной страницы** → ответ остаётся
   ответом (компактная цитата), а не превращается в блок пересылки. Это главный баг, ради
   которого всё делалось.
3. Отредактировать оригинал → текст в цитате обновляется (у всех, кто отвечал).
4. Удалить оригинал → цитата показывает «Сообщение удалено», без текста и без перехода.
5. Ответить на сообщение чужого чата по прямому id → `MessageNotFound` (и то же самое
   на несуществующий id — ответы должны быть неотличимы).
6. Ответ без текста и без файлов → сообщение не отправляется.

### Пересылка

7. Переслать одно сообщение → как раньше.
8. **Переслать несколько сообщений одним действием** → у получателя видны все, в порядке
   отправителя. Проверить и Android, и Web, и WinUI: каждый рисует блок на каждое сообщение.
9. Переслать больше 20 → `TooManyForwardedMessages`.
10. Переслать в чат, к которому нет доступа к оригиналу → `NoAccessToChat`.
11. Переслать уже пересланное (пачку) → уходят оригиналы, а не снапшот, и все, а не первый.
12. Пересылка внутри того же чата → показывается как пересылка, а не как ответ.

### Совместимость (важно)

13. **iOS, macOS, Linux и ClientV2.WPF не обновлялись.** Они шлют устаревшее
    `forwarded_message_id`, и оно должно работать как прежде: отправка идёт, сообщение
    приходит, рендерится как пересылка.
14. Старые сообщения в БД (ответы, лежащие там как forward-снапшоты) продолжают
    отображаться как раньше — мигрировать их нельзя, в базе они неотличимы от пересылок.
15. Смешать старое поле с новым в одном запросе → `InvalidArgument`.

### Федерация

16. Ответ в федеративном DM → на партнёрской ноде цитата на месте.
17. Ответ на сообщение, которого у партнёра ещё нет → сообщение доставляется **без** цитаты,
    без бесконечного RETRY.
18. Пересылка в федеративном DM → снапшот доезжает (текст, автор, вложения).
19. Битый снапшот пересылки от чужой ноды → отклоняется permanent (REJECTED), не RETRY.

### Черновики

20. Выбрать reply, набрать текст, уйти из чата, вернуться → reply восстановился.
21. Отправить из черновика → уходит **ответом**, а не пересылкой. Раньше это было главное
    расхождение: черновик хранил `ReplyToMessageId`, а отправка конвертировала его в forward.

---

## Замечание, которое я не чинил

`SendMessageCommandHandler` считает сообщение пустым по `Text is null`. Через gRPC
`Text` никогда не `null` — proto3 отдаёт `""`. То есть проверка на пустое сообщение с
клиентов фактически недостижима и была такой **до** этой задачи. Я сохранил поведение
как есть, чтобы не менять контракт вне объёма задачи, но чинить это, вероятно, стоит:
достаточно сравнивать с `string.IsNullOrEmpty`.

---

## Как поднять окружение, если что-то из инструментов отсутствует

Пригодилось в этой сессии:

```bash
# .NET 10 SDK есть в архиве Ubuntu 24.04
apt-get install -y dotnet-sdk-10.0

# dotnet-ef обязан совпадать с версией EF Core в проекте (10.0.8)
dotnet tool install --global dotnet-ef --version 10.0.8
```

Оговорка про `dotnet ef`: транзитивно в `BarkFluff.Messages` подтягивается
`Microsoft.EntityFrameworkCore.Design` **8.0.0** при EF Core 10.0.8, и генерация миграции
падает с `MissingMethodException`. Чтобы сгенерировать миграцию, я временно добавлял
`PackageReference Microsoft.EntityFrameworkCore.Design 10.0.8` в csproj и убирал обратно —
в коммит эта правка не попала. Возможно, стоит закрепить Design 10.0.8 в проекте насовсем,
но это отдельный вопрос, не относящийся к reply/forward.

Для web-бандла нужен **protoc 3.20.3** (именно он пинится в `Dockerfile.slim`): в 3.21
убран встроенный JS-генератор, и `generate-proto.sh` падает на `protoc-gen-js: program not found`.
