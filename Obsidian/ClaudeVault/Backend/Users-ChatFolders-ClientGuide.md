# ChatFolders — контракт клиента

Папки — принадлежащие пользователю списки ID чатов с порядком сортировки. RPC находятся в `UsersApi` файла `Shared/BarkFluff.Proto/users_api.proto`. Пути к коду Users ниже указаны относительно `Backend/BarkFluff.Users/`; `Shared/` — от корня репозитория.

## Данные и синхронизация

`ChatFolderData`: `folder_id` (Guid), `folder_name`, `folder_icon` (пусто, если не задана), `chat_list` (Guid чатов строками), `sort_order` (меньше — выше). `GetChatFolders` возвращает только папки текущего пользователя в порядке `sort_order`, затем внутреннего ID записи. Stream изменений папок нет; для синхронизации с другим устройством вызовите `GetChatFolders`.

## Семантика RPC

| RPC | Поведение |
|---|---|
| `GetChatFolders` | Пустой запрос; возвращает папки текущего пользователя. |
| `CreateChatFolder` | Обрезает пробелы в имени; длина после trim — 1–64. Обрезает пробелы в иконке; пустая превращается в unset. Создаёт пустой список с `max(sort_order)+1`. |
| `UpdateChatFolder` | `folder_name` и `folder_icon` optional. Не задано — оставить как есть; заданная пустая иконка очищает поле. `has_chat_list_update=true` заменяет весь список, включая замену на пустой. |
| `DeleteChatFolder` | Удаляет папку; чаты в Messages не меняются. |
| `AddChatToFolder` / `RemoveChatFromFolder` | Меняют один chat ID; добавление уже существующего и удаление отсутствующего — no-op. Ответ содержит текущее состояние папки. |
| `ReorderChatFolders` | Применяет переданные `sort_order` к папкам владельца. Невалидные Guid строки пропускаются; неизвестные и чужие ID игнорируются. Ответ пустой. |

Чтение и мутации ограничены ID пользователя из токена. Невалидный, отсутствующий или чужой folder ID даёт один и тот же `ChatFolderNotFoundException`. Chat ID должен быть Guid, но Users не проверяет его через Messages; один чат может быть в нескольких папках.

`UpdateChatFolder` с `has_chat_list_update=true` заменяет весь список и может затереть параллельные изменения другого устройства. Для изменения одного элемента используйте `AddChatToFolder`/`RemoveChatFromFolder`.

## Ошибки

- `ChatFolderNotFoundException`: `5F0B7B2E-3F6E-4D2B-9B9E-9B7E7C2B9D8A`.
- `ChatFolderInvalidNameException`: `8C1A6F4D-1B22-4E1B-8E4D-7E9A5B6C2A11` для пустого имени или имени длиннее 64 после trim.
- Невалидный chat Guid возвращает Messages `ChatIdNotValidException`.

Код: `Host/UsersApiService.cs`, `Features/ChatFolders/`, `Persistence/Services/ChatFolderStorage.cs`. Связанные заметки: [[Backend/Users]], [[Backend/Messages]].
