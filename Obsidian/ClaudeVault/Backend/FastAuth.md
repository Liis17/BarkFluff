# BarkFluff.FastAuth

QR-вход нового устройства. Контракт: Shared/BarkFluff.Proto/fast_auth_api.proto; код: Backend/BarkFluff.FastAuth/. Сервис использует Redis для общего состояния между инстансами и XAuth для защищённых RPC.

## Поток и статус

GenerateFastAuthToken и SubscribeFastAuthResult анонимны. Клиент генерирует QR на 5 минут и подписывается на stream. Авторизованный User сканирует QR, получает метаданные устройства и одноразовый confirmation_code, затем может принять или отклонить запрос. Stream возвращает финальный статус и access/refresh tokens только при Accepted.

Статусы: Pending=1, Scanned=2, Accepted=3, Rejected=4, Expired=5, TelegramPending=6. При включённом для FastAuth Telegram confirmation Identity сначала ставит challenge: AcceptFastAuth фиксирует TelegramPending и возвращается; stream/polling продолжает Identity completion. Без подтверждения Identity создаёт сессию сразу.

## Состояние и гонки

Redis key fastauth:session:{id}; все изменения статуса атомарны через Lua и проверяют срок, пользователя, код и допустимый переход. Логический TTL QR — 5 минут; Redis хранит ключ ещё 30 секунд, чтобы отличать Expired от NotFound. Финальное состояние/токены хранятся 30 секунд для переподключившегося клиента. На сессию допускается один подписчик глобально, с блокировкой в Redis; события будят локальный stream, состояние перечитывается из Redis.

ClientDeviceId исходного устройства отделён от одноразового QR session ID и передаётся в Identity для выпуска refresh-сессии. Параллельные Accept могут получить одну Identity-сессию; completion не отзывает токены победившего перехода. Если выпуск пришёлся на отклонённую/истёкшую сессию, проигравший путь отзывает созданную device session.

FastAuthServerApi.GetFastAuthInfo требует Service JWT, но сейчас отвечает gRPC Unimplemented. Внешний QR сессии не продлевается при reconnect.
