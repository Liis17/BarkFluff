# BarkFluff.Shared.Identity — карта проекта

Исходники: `Shared/BarkFluff.Shared.Identity/`; `net10.0`, без внешних NuGet-зависимостей. Числа `ServiceId` и строковые claim names — стабильные контракты; новые IDs добавлять в конец.

## ServiceId

| Enum | ID | Сервис |
|---|---:|---|
| Unknown | 0 | не задан |
| Identity | 1 | аутентификация |
| Users | 2 | профили и устройства |
| Beacon | 3 | информация о ноде |
| Notifications | 4 | email worker |
| Files | 5 | файлы |
| Messages | 6 | чаты и сообщения |
| FastAuth | 7 | вход нового устройства |
| Updates | 8 | real-time события |
| Onliner | 9 | presence |
| CloudMessaging | 10 | push |
| Web | 11 | gRPC-Web proxy |
| Developers | 12 | документация API |
| Calls | 13 | звонки |
| Bots | 14 | bot API |
| Federation | 15 | межсерверная федерация |

## TokenType и claims

`TokenType`: `Unknown=0`, `User=1`, `Service=2`, `Bot=3`.

`IdentityClaims`: `UserId=x-user-id`, `TokenType=x-token-type`, `ServiceId=x-service-id`, `DeviceId=x-device-id`, `BotTokenId=x-bot-token-id`.

Использование: [[Backend/GrpcServer]] валидирует токены и строит XAuth policies; [[Backend/Identity]] выпускает JWT; Settings каталогизирует значения `ServiceId`. Краткое описание: [[Shared/Identity]].
