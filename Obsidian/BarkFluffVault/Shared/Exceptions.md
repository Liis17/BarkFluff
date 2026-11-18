# BarkFluff.Shared.Exceptions

Общие gRPC-ошибки в `Shared/BarkFluff.Shared.Exceptions/`. `BaseGrpcException` задаёт значения по умолчанию: код `BDF4009D-24D0-4E0C-A10C-AEF33E0D0022`, сообщение «Неизвестная ошибка», статус `FailedPrecondition`.

Для unary RPC `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs` записывает `ErrorCode` в trailer `x-error-code`, а `ErrorMessage` — в gRPC detail. `ExceptionClientInterceptor` сопоставляет trailer с конкретным классом ошибки по коду; он обрабатывает только async unary-вызовы. Неизвестный код остаётся обычным `RpcException`. Все наследники должны иметь parameterless constructor для загрузки через reflection.

Коды ошибок — wire contract. В частности, `FederatedDmRejectedException` использует строковый `FederatedDmRejected`, а не GUID. Текущий реестр кодов и исключений со специальным gRPC-статусом: [[Shared/Exceptions-ProjectMap]].

Ограничения аккаунта используют `AccountBlockedException` (`F34D8C22-C075-4E9C-92AD-C893F192D8FA`) и `AccountPendingDeletionException` (`8E6F0F30-98AF-4F96-A63B-20D52AEEFB6A`) в Identity legacy/session RPC, а Users сообщает о недопустимом переходе через `InvalidAccountRestrictionTransitionException` (`A58D2B61-EC71-4D62-9679-07D7C7F6D641`). Запрет личного взаимодействия в Messages/Calls возвращает `UserUnavailableException` (`6BC0E7A1-5117-4910-9717-1784E6921F6F`). GUID сопоставляются динамически через `ExceptionClientInterceptor`; challenge sign-in вместо исключения возвращает `error_code` в protobuf.

Серверная проверка username в `Backend/BarkFluff.Users/Services/UsernameFormatValidator.cs` допускает только `^[a-zA-Z0-9_]{3,32}$`; дефис не допускается. Нарушение валидации возвращает `UsernameInvalidFormatException` с кодом `E7A4C9D2-3B61-4F82-A5E0-9C1D8F2B6A47`.
