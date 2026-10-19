# BarkFluff.Identity — карта проекта

Потоки и RPC контракты: [[Backend/Identity]] · ключевые метрики: [[Backend/Identity-Metrics]]. Исходники: Backend/BarkFluff.Identity/.

- Program.cs — Settings, PostgreSQL, Redis, gRPC, MassTransit, XAuth и hosted workers.
- Host/IdentityApiService*.cs — публичный IdentityApi; Host/IdentityServerApiService.cs — service-to-service API с Service policy.
- Services/AuthenticationService*.cs — challenge-based sign-in/registration, Telegram/FastAuth, proof-gated account security и notifications.
- Features/ и Persistence/ — legacy RPC flows, хранилища и доменные модели; Persistence/Migrations/ — schema.
- Security/ — request abuse guard, security options и failure results.
- Infrastructure/TelegramAuthWorker.cs — обработка входов через Telegram; NotificationQueueSender.cs и Consumers/SessionRevokedConsumer.cs — интеграции событий.
- Контракт: Shared/BarkFluff.Proto/identity_api.proto.

Не объединяйте новый challenge API с legacy Auth/OTP RPC: они остаются разными контрактными потоками.
