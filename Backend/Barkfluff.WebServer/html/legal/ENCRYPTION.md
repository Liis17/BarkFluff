# Техническое описание шифрования BarkFluff

**Дата вступления в силу:** 24 января 2026 г.  
**Последнее обновление:** 17 июня 2026 г.

## 1. Введение

Эта страница описывает только те механизмы защиты и шифрования, которые присутствуют в текущей архитектуре BarkFluff.

## 2. Передача данных и авторизация

- Клиенты взаимодействуют с backend через gRPC или gRPC-Web.
- Внешний HTTP/gRPC-трафик проходит через HTTPS/TLS на публичном контуре.
- Внутренние события между сервисами передаются через RabbitMQ / MassTransit.
- Авторизация gRPC-запросов использует XAuth metadata, включая `x-auth-token` и device-заголовки.

## 3. JWT, refresh-токены и 2FA

- Access token — JWT, подписанный ключом из конфигурации Identity.
- Время жизни access token задается `JwtSettings:ExpiryMinutes`.
- Refresh token хранится в `Identity.RefreshTokens` и привязан к Device ID.
- Сессии можно просматривать и отзывать через `GetActiveSessions` и `RemoveActiveSession`.
- 2FA поддерживает TOTP authenticator и email-коды.

## 4. Обычные сообщения

Обычные личные и групповые сообщения хранятся в Messages-сервисе. Серверная логика использует эти данные для списка чатов, истории сообщений, read receipts, редактирования, удаления, экспорта и realtime-обновлений.

## 5. Private-чаты

Private-чаты — это 1-к-1 encrypted-чаты через passphrase.

- Клиент создает соль `kdf_salt` и `passphrase_verifier`.
- Ключ выводится на клиенте из passphrase и соли чата.
- Сообщение отправляется как `ciphertext`, `nonce` и `associated_data`.
- Сервер хранит `EncryptedMessage` как opaque blob и не получает passphrase.
- Редактирование private-сообщения заменяет ciphertext/nonce/AAD.
- Удаление private-сообщения помечает его удаленным и очищает ciphertext.

## 6. Secret-чаты

Secret-чаты — это 1-к-1 чаты между конкретными устройствами.

- Устройства регистрируют prekey bundle: identity key, signed prekey и one-time prekeys.
- Инициатор получает prekey bundle устройства собеседника через `FetchPrekeyBundle`.
- Сообщения передаются как opaque `SecretEnvelope`.
- Сервер не создает обычный Chat и не сохраняет историю secret-чата в Messages DB.
- Envelope ретранслируется получателю и временно буферизуется с TTL 24 часа, если получатель офлайн.
- После `AckSecretMessage` envelope удаляется из буфера.

## 7. Файлы

Файлы, аватары, постеры, вложения и превью обслуживаются Files-сервисом и хранятся в Minio/S3-compatible хранилище. Метаданные файлов используются для скачивания, превью, экспорта и отображения в чатах.

## 8. Локальное хранение клиентов

| Клиент | Что используется |
| --- | --- |
| Windows WPF | `GlobalParam.json`, PIN-защита, локальные настройки клиента |
| Android | `GlobalParam`, локальные настройки, Firebase token, key/prekey данные для encrypted/secret-чатов |
| macOS | Keychain для токенов и локальные настройки приложения |
| Web | gRPC-Web, cookies только для технических сценариев web-клиента и перехода с публичной страницы профиля |

## 9. Push-уведомления

CloudMessaging получает события из RabbitMQ и отправляет push-уведомления через Firebase Cloud Messaging. Для secret-сообщений push не содержит текст сообщения.

## 10. Контакты

- Безопасность: security@barkfluff.com
- Приватность: privacy@barkfluff.com
