# BarkFluff.FastAuth — карта проекта

Основной контракт и гонки состояния: [[Backend/FastAuth]]. Исходники: Backend/BarkFluff.FastAuth/.

- Program.cs / DependencyInjection.cs — Settings, Redis session store, XAuth, Health, клиент Identity.
- Domain/FastAuthSessionState.cs и FastAuthSessionStore.cs — типизированное состояние и интерфейс хранилища.
- Infrastructure/RedisFastAuthSessionStore.cs — Redis операции с атомарными Lua переходами; FastAuthEventBus.cs — уведомление подписчиков.
- Features/ — generate, scan, accept, reject и subscribe handlers.
- Host/FastAuthApiService.cs — клиентские RPC; FastAuthServerApiService.cs — служебное завершение в Identity.
- Infrastructure/FastAuthCompletion.cs и QrCodeGenerator.cs — completion и QR код.

Хранилище больше не является in-memory менеджером с периодическим expiration worker: состояние и конкурирующие переходы обслуживает Redis store.
