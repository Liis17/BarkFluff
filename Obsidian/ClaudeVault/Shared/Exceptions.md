# BarkFluff.Shared.Exceptions

Общие gRPC-ошибки в `Shared/BarkFluff.Shared.Exceptions/`. `BaseGrpcException` задаёт значения по умолчанию: код `BDF4009D-24D0-4E0C-A10C-AEF33E0D0022`, сообщение «Неизвестная ошибка», статус `FailedPrecondition`.

Для unary RPC `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs` записывает `ErrorCode` в trailer `x-error-code`, а `ErrorMessage` — в gRPC detail. `ExceptionClientInterceptor` сопоставляет trailer с конкретным классом ошибки по коду; он обрабатывает только async unary-вызовы. Неизвестный код остаётся обычным `RpcException`. Все наследники должны иметь parameterless constructor для загрузки через reflection.

Коды ошибок — wire contract. В частности, `FederatedDmRejectedException` использует строковый `FederatedDmRejected`, а не GUID. Текущий реестр кодов и исключений со специальным gRPC-статусом: [[Shared/Exceptions-ProjectMap]].

Серверная проверка username в `Backend/BarkFluff.Users/Services/UsernameFormatValidator.cs` допускает только `^[a-zA-Z0-9_]{3,32}$`; дефис не допускается. Нарушение валидации возвращает `UsernameInvalidFormatException` с кодом `E7A4C9D2-3B61-4F82-A5E0-9C1D8F2B6A47`.
