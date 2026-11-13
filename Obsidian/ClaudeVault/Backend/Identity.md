# BarkFluff.Identity

Сервис учётных записей, authentication challenges, 2FA и сессий. Порт по умолчанию Settings — 7000. Внешний контракт: Shared/BarkFluff.Proto/identity_api.proto. Карта исходников: [[Backend/Identity-ProjectMap]], метрики: [[Backend/Identity-Metrics]].

## API и модели входа

IdentityApi содержит как legacy RPC Auth/CreateToken/CreateAccount/ConfirmAccount, пароль/OTP/reset и сессии, так и challenge API: GetAuthCapabilities, BeginRegistration/BeginSignIn, GetAuthChallenge, Complete/Cancel/ResendAuthChallenge, reauthentication, password recovery, recovery codes, email/Telegram binding и настройки уведомлений. Это сосуществующие контрактные потоки; не считать challenge API простой заменой имён legacy RPC.

`ResolveSignIn` возвращает только действующий `AuthenticationPolicy.Mode` для известного аккаунта; неизвестный, draft и bot дают `account_not_found` и `LOGIN_MODE_UNSPECIFIED`. Вызов требует metadata устройства и проходит abuse guard с `sending=false`; он читает пользователя и настройки, не создавая challenge и не отправляя код/уведомление. Результат служит подсказкой интерфейсу, а не доказательством входа. `BeginSignIn.report_login_mode_mismatch=true` позволяет вернуть `account_not_found` или `login_mode_disabled` до проверки пароля при неизвестном аккаунте или устаревшем выборе. При совпадении режима все обычные проверки пароля и второго фактора остаются обязательными; поле по умолчанию `false` сохраняет прежнее поведение. Развёртывать backend до обновления Web-клиента.

IdentityServerApi — отдельный service-to-service API и class-level Service policy. Клиентские RPC требуют policy выборочно: наличие XAuth на host не означает, что каждый метод принимает только User или что все методы защищены одинаково. См. атрибуты Host/IdentityApiService*.cs.

JWT и client metadata передаются по правилам [[Backend/GrpcServer]]. Redis используется для распределённого abuse guard и Telegram lease; PostgreSQL — для учётных данных, OTP, refresh tokens и challenges. Миграции запускаются при старте.

## Telegram, FastAuth и уведомления

Telegram login и Telegram confirmation доступны только при настроенном Settings TelegramAuth:Enabled/BotToken/NodeName. TelegramAuthWorker принимает callback от бота под Redis lease и передаёт challenge в Identity; worker отслеживает Telegram updates с PostgreSQL offset. FastAuth обращается к IdentityServerApi для выпуска device session; если для fast auth включено Telegram подтверждение, запрос остаётся ожидать challenge до завершения.

Email queue producer обслуживает Email notification path. Login notification channel может быть Telegram: Identity отправляет такое уведомление напрямую через node bot, email-события идут через Notification. Нода должна иметь хотя бы один настроенный канал, разрешённый политикой аккаунта.

Security gates для изменения факторов/данных аккаунта требуют proof challenge; recovery codes хранятся как расходуемые credentials. Abuse guard ограничивает рискованные операции в Redis, так что лимиты общие для инстансов. Политики и параметры берутся из IdentitySecurity в Settings.

## Ограничения аккаунта и окончательное удаление

`IdentityServerApi.SetAccountRestrictionServer` сначала меняет статус через Users, затем выполняет отзыв сессий и устройств, отменяет незавершённые authentication challenges, публикует `UserAccountStatusChanged` для Bots и Messages и отправляет status email через Notification. Запись статуса и публикация событий не атомарны; при ошибке публикации повтор того же идемпотентного действия администратора публикует их снова. Для Block/ScheduleDeletion Identity собирает device IDs из refresh tokens и Users, публикует `SessionRevokedEvent` каждому устройству с сроком access JWT + 2 минуты, затем удаляет refresh tokens и устройства. Для Unblock/CancelDeletion refresh tokens удаляются и сессии не восстанавливаются, поэтому вход всегда начинается заново. CancelDeletion возвращает прежний статус: ранее заблокированный аккаунт остаётся Blocked с исходной причиной, что отражается в письме; Bots остаётся ограничен. Сбой отправки email не отменяет изменение статуса.

Challenge-вход сообщает причину только в `CompleteAuthChallengeResponse` после доказательства фактора: `account_blocked` или `account_pending_deletion`, без выдачи сессии. Начало входа и восстановления пароля состояние не раскрывают. Legacy/session RPC используют `AccountBlockedException` и `AccountPendingDeletionException`.

`Infrastructure/AccountDeletionWorker.cs` раз в 10 минут под Redis lease вызывает атомарный `Users.ClaimAccountsForDeletion`, затем очищает данные Identity транзакцией и последовательно удаляет профильные файлы в Files и ботов владельца в Bots. `Users.CompleteAccountCleanup` отмечает завершение и вызывается только после успешного прохождения всех шагов; сбой оставляет Deleted-аккаунт доступным для повторной очистки. Статус Users и публикация события Identity не атомарны; восстановление — повтор того же идемпотентного действия администратором. Финальное удаление не отправляет email. Запись пользователя, чаты и история собеседников остаются; сообщения и вложения не удаляются. Связанные потоки: [[Backend/Users]], [[Backend/Files]], [[Backend/Bots]], [[Backend/Notification]].
