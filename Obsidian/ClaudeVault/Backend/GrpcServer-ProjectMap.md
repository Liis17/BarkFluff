# BarkFluff.GrpcServer — карта проекта

Поведение библиотечных контрактов: [[Backend/GrpcServer]]. Исходники: Backend/BarkFluff.GrpcServer/.

- WebApplicationBuilderExtensions.cs — Settings загрузка и конфигурация listener.
- ServiceCollectionExtensions.cs — gRPC interceptors, request context и типизированные Settings.
- XAuth/ — JWT validation, policies и token revocation cache.
- Tracker/ — извлечение client metadata в RequestContext.
- Metrics/ — потокобезопасный collector и reporter.
- HealthEndpointExtensions.cs, HealthServiceCollectionExtensions.cs, ReadinessMonitorService.cs — liveness/readiness.
- ServerExceptionInterceptor.cs, SerilogExtensions.cs, ActivityLogEnricher.cs — ошибки, логирование и trace enrichment.
