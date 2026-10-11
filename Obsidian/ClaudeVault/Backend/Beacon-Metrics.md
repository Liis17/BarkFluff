# BarkFluff.Beacon — метрики

Общий формат: [[Backend/GrpcServer]]. Источники метрик Beacon: Backend/BarkFluff.Beacon/Host/BeaconApiService.cs, Features/GetServerInfo/GetServerInfoCommandHandler.cs, BackgroundServices/ServerRegistrationService.cs.

## Текущие метрики

- GetServerInfo: server_info_requests, server_info_success, server_info_errors, server_info_duration_ms_total, last_server_info_request_unix.
- Чтение настроек (10 сервисов, включая Federation): configuration_fetch_success, configuration_fetch_errors.
- Регистрация в Navigator: navigator_registrations, navigator_registration_errors, navigator_registration_duration_ms_total, last_navigator_registration_unix, navigator_registration_healthy.
- Запуск: service_started_unix; navigator_registration_healthy стартует со значением 0.

Beacon использует профиль ImmediateByDefault: counter-события экспортируются сразу. Изменившиеся gauges отправляются при буферном flush не реже чем через 10 секунд; неизменившиеся gauges не повторяются. Payload SchemaVersion=2, поля Counters и Gauges. Это не снимок «за каждые 5 секунд».
