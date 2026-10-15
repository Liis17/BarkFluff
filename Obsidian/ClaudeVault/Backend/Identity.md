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
