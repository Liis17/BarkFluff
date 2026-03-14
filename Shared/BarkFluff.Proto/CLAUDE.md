# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

`BarkFluff.Proto` — единственный проект в этой директории. Содержит все `.proto`-контракты платформы BarkFluff. Сборка генерирует C# gRPC-классы, используемые всеми микросервисами и клиентами.

## Build

```bash
dotnet build BarkFluff.Proto.csproj
```

> Проект не содержит рукописного C# кода — только `.proto` файлы и `.csproj`. Код генерируется Grpc.Tools при сборке.

## Proto Files Overview

| Файл | C# namespace | Назначение |
|------|-------------|------------|
| `shared.proto` | `BarkFluff.Proto.Shared` | Общие типы: `Message`, `MessageContent`, `MessageAttachment`, `MessageAttachmentType`, `PageRequest` |
| `identity_api.proto` | `BarkFluff.Proto.Identity` | Auth, 2FA, сессии, сброс/смена пароля |
| `users_api.proto` | `BarkFluff.Proto.Users` | Профили, устройства, баджи, поиск |
| `messages_api.proto` | `BarkFluff.Proto.Messages` | Чаты, сообщения, вложения |
| `files_api.proto` | `BarkFluff.Proto.Files` | Upload/download URL, хранилище, бейджи |
| `updates_api.proto` | `BarkFluff.Proto.Updates` | Real-time стримы: новые сообщения, прочтения |
| `onliner_api.proto` | `BarkFluff.Proto.Onliner` | Онлайн-статусы (стрим + polling) |
| `fast_auth_api.proto` | `BarkFluff.Proto.FastAuth` | QR/текстовая авторизация, подключение устройств |
| `beacon_api.proto` | `BarkFluff.Proto.Beacon` | Точка входа: описание сервера и адреса микросервисов |
| `navigator_api.proto` | `BarkFluff.Proto.Navigator` | Глобальный реестр серверов BarkFluff |
| `configuration_api.proto` | `BarkFluff.Proto.Configuration` | Централизованная конфигурация, зарезервированные имена |

## Service Pairs Pattern

Большинство `.proto` файлов определяют два сервиса:
- `XxxApi` — публичный, вызывается клиентами (JWT user token)
- `XxxServerApi` — внутренний, вызывается только другими микросервисами (service token)

Исключения: `UpdatesApi`, `OnlinerApi`, `NavigatorApi`, `BeaconApi`, `ConfigurationApi` — только один сервис.

## Key Design Notes

- **`shared.proto`** импортируется как зависимость в `messages_api.proto`, `updates_api.proto`, `users_api.proto`. При добавлении типов, используемых в нескольких сервисах, помещать их сюда.
- **`MessageAttachmentType`** enum живёт в `shared.proto` (не в `messages_api.proto`), т.к. используется и в `files_api.proto`.
- **`CreateTokenResponse.access_token`** имеет field_number=**2** (не 1) — важно при ручной десериализации.
- **`AuthRequest` и `FindByLoginRequest`** используют `oneof login` (username ИЛИ email).
- **`SendMessageRequest`** использует `oneof source_id` (chat_id ИЛИ user_id).
- **`ListMessagesRequest`**: поле `count` устарело, использовать `offset_before`/`offset_after`.
- **`MessageReadEvent.new_read_by`**: содержит **полный** список прочитавших, не только новых.
- **`UploadFileType`** enum в `files_api.proto` — отдельный от `MessageAttachmentType`, нужно маппировать при отправке сообщений.

## Using in Projects

```xml
<!-- Серверная сторона -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\xxx_api.proto" GrpcServices="Server" />

<!-- Клиентская сторона -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\xxx_api.proto" GrpcServices="Client" />

<!-- Только типы (без сервисного кода) -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\shared.proto" GrpcServices="None" />
```