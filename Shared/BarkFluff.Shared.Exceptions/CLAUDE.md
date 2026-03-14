# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

`BarkFluff.Shared.Exceptions` — общая библиотека исключений для всех микросервисов BarkFluff. Используется как на серверной стороне (бросает исключения через gRPC trailers), так и на клиентской (перехватывает и преобразует `RpcException` обратно в типизированные исключения).

## Build

```bash
dotnet build BarkFluff.Shared.Exceptions.csproj
```

## Architecture

### Базовый класс

`BaseGrpcException` — базовый класс для всех исключений. Каждый наследник задаёт два свойства:

- `ErrorCode` — уникальный GUID-строка, передаётся в gRPC trailer `x-error-code`
- `ErrorMessage` — читаемое описание ошибки на русском

### Организация исключений по папкам

Исключения сгруппированы по доменам (по сервисам):

- `Identity/` — аутентификация, OTP, сессии, пользователи
- `Messages/` — чаты и сообщения
- `Files/` — загрузка файлов
- `Users/` — профили пользователей
- `Navigator/` — регистрация серверов

### ExceptionClientInterceptor

`Interceptors/ExceptionClientInterceptor.cs` — gRPC клиентский интерцептор. При получении `RpcException` с trailer `x-error-code`:

1. Загружает все наследники `BaseGrpcException` через рефлексию (кешируется в `CachedExceptions`)
2. Ищет совпадение по `ErrorCode`
3. Бросает найденное типизированное исключение вместо `RpcException`

Подключается на клиенте: `.AddInterceptor(() => new ExceptionClientInterceptor())`.

### Добавление нового исключения

1. Создать класс в соответствующей папке домена
2. Унаследовать от `BaseGrpcException`
3. Переопределить `ErrorCode` (новый GUID) и `ErrorMessage`
4. На сервере — бросать это исключение, `ServerExceptionInterceptor` (в `BarkFluff.GrpcServer`) автоматически упакует его в gRPC trailer

```csharp
public class MyNewException : BaseGrpcException
{
    public override string ErrorCode => "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx";
    public override string ErrorMessage => "Описание ошибки";
}
```
