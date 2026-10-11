# BarkFluff.Beacon — карта проекта

Основное описание: [[Backend/Beacon]]. Исходники: Backend/BarkFluff.Beacon/.

- Program.cs — загрузка конфигурации, gRPC listener, клиенты Settings и Navigator, метрики и readiness.
- Host/BeaconApiService.cs — RPC GetServerInfo.
- Features/GetServerInfo/ — параллельно читает конфигурацию служб и собирает protobuf-ответ.
- BackgroundServices/ServerRegistrationService.cs — каждые 5 минут регистрирует ноду в Navigator; без ExternalEndpoint:Host пропускает регистрацию.
- Configurations/ — свойства ноды и цвет.
- Контракт: Shared/BarkFluff.Proto/beacon_api.proto; данные регистрации: navigator_api.proto.

Ответ Beacon берёт сервисы Identity, Users, Files, Messages, Updates, Onliner, FastAuth, Calls и Bots из Settings; дополнительно читает Federation для ServerName/Enabled. Итого 10 запросов конфигурации, но 9 полей Service в ответе.
