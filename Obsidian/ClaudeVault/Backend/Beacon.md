# BarkFluff.Beacon

Публичная точка discovery-конфигурации ноды. Слушает RunSettings:Port (Settings default 7002); GetServerInfo отдаёт единый контракт из [[Shared/Proto]]. Подробная карта: [[Backend/Beacon-ProjectMap]], метрики: [[Backend/Beacon-Metrics]].

## Поведение

GetServerInfo читает из Settings конфигурацию Identity, Users, Files, Messages, Updates, Onliner, FastAuth, Calls и Bots, а также Federation metadata; ответ кешируется на 5 минут. Сервисные endpoint'ы строятся из ExternalEndpoint:Host как HTTPS на 443, с TlsEnabled=true и Status=Healthy. Это статус, сформированный из наличия адреса, а не активная health-проба удалённого сервиса.

В ответ также входят имя и свойства сервера, цвета, LiveKit:PublicUrl, Federation:ServerName/Enabled и необязательный ExternalEndpoint:MediaHost для Files. Незаполненный внешний адрес службы означает Offline.

ServerRegistrationService каждые 5 минут отправляет свой публичный Beacon origin, свойства ноды и доступные Web/Files media endpoints в Navigator. При отсутствии ExternalEndpoint:Host регистрация пропускается. Гостевой GetServerInfo RPC не использует XAuth; у сервиса есть общий readiness endpoints.

Сборка: dotnet build Backend/BarkFluff.Beacon/BarkFluff.Beacon.csproj.
