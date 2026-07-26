# BarkFluff.Shared.Exceptions — Карта файлов

Полный перечень файлов проекта `Shared/BarkFluff.Shared.Exceptions/`.
Связанная документация: [[Shared/Exceptions]]

---

## Корневые файлы

| Файл | Описание |
|------|----------|
| `BarkFluff.Shared.Exceptions.csproj` | Проект библиотеки, target: `net10.0`, Nullable enable |
| `BaseGrpcException.cs` | Базовый класс всех исключений. Содержит `ErrorCode` (GUID-строка) и `ErrorMessage`. По умолчанию ErrorCode = `BDF4009D-24D0-4E0C-A10C-AEF33E0D0022`, ErrorMessage = "Неизвестная ошибка" |

---

## Interceptors/

| Файл | Описание |
|------|----------|
| `ExceptionClientInterceptor.cs` | gRPC клиентский interceptor. Перехватывает `RpcException` с trailer `x-error-code`, ищет совпадение среди кешированных наследников `BaseGrpcException` (через рефлексию + `Activator.CreateInstance`), бросает типизированное исключение. Кеш — статическое поле `CachedExceptions`. |

---

## Identity/

Исключения домена аутентификации, авторизации, управления аккаунтом.

| Файл | ErrorCode | ErrorMessage |
|------|-----------|--------------|
| `ConfirmationCodeExpiredException.cs` | `7AABF347-1210-4B14-A93B-2BA8574D74E7` | Код подтверждения больше недействителен |
| `ConfirmationCodeIncorrectException.cs` | `4396D597-D605-4040-AF0F-D9168F0CA034` | Неверный код подтверждения |
| `ConfirmationCodeNotFoundException.cs` | `56D9BB63-DA40-40DE-9C56-7487A1A437D0` | Код потверждения не найден |
| `EmailExistException.cs` | `7599F3F1-C2EC-4D05-BF38-A1A60D40BA4E` | Пользователь с таким емейлом зарегристирован |
| `InvalidLoginOrPasswordException.cs` | `21BFB9B5-C377-45D1-9B15-6B7F3432B397` | Неверный логин или пароль |
| `InvalidOldPasswordException.cs` | `A7E3F1B2-9C4D-4E8A-B5F6-2D1A3C7E9F04` | Неверный старый пароль |
| `InvalidRefreshTokenException.cs` | `7E6A31C5-3C4D-412E-87BC-0A387617A5D3` | *(ErrorMessage не переопределён — используется базовое "Неизвестная ошибка")* |
| `NotSetUsernameOrEmailException.cs` | `55872FA3-4F77-4C5A-B471-C25699BA20C0` | Не передан ни логин ни email |
| `NotValidOtpCodeException.cs` | `803B632C-4457-4B05-9435-9C3DD0F41E00` | Неверный код 2FA |
| `OtpCodeNeedException.cs` | `C1576884-12D8-4722-A7EE-9F9789AD1265` | Необходимо 2FA ввести код |
| `OtpNotCreatedException.cs` | `A0D92E59-DC33-4072-BFDE-12E7E26FAAD0` | Для вызова этого метода, необходимо создать otp |
| `ResetIdExpiredException.cs` | `9F3D1B82-8E55-4C71-BD2A-3D7FAC2E6AE1` | Срок действия идентификатора сброса пароля истёк |
| `ResetIdHasIsApprovedException.cs` | `BE708516-BF40-44F9-A6D1-A7F30AB02BED` | Невозможно повторно сбросить пароль по этому идентификатору сброса |
| `ResetIdNotFoundException.cs` | `5B9A8269-617E-4D4C-9696-A554C59E3A86` | Неверный идентификатор кода сброса пароля |
| `SessionNotFoundException.cs` | `011BF29A-2DE6-4A63-BF8D-3F36AE730D9D` | Сессия не найдена |
| `UsernameExistException.cs` | `DB157CD8-98A3-4A35-9857-33821813D422` | Пользователь с таким именем пользователя существует |
| `UsernameInvalidFormatException.cs` | `E7A4C9D2-3B61-4F82-A5E0-9C1D8F2B6A47` | Имя пользователя имеет недопустимый формат (latin/цифры/_, 3–32 симв.) |
| `UsernameOrEmailIsEmptyException.cs` | `84EC96DA-2A1A-499E-ACED-C1444E07E0E6` | Передан пустое имя пользователя или емайл |
| `UsernameReservedException.cs` | `A3F1B2C4-7D8E-4F5A-9B6C-1E2D3F4A5B6C` | Это имя пользователя зарезервировано и не может быть использовано |
| `UserNotFoundException.cs` | `A4DAB334-1067-4838-A782-C4257DC838F7` | Пользователь не найден |
| `XAppInfoIsRequiedException.cs` | `FFE79950-5668-4786-A834-6B490650FE62` | Этот запрос требует передачи x-app-name и x-app-version |
| `XDeviceNameIsRequiredException.cs` | `4E98408C-C969-4737-936B-A2AABB05B88D` | Этот запрос требует передачу x-device-name |
| `XOsNameIsRequiredException.cs` | `575EBB8D-5687-40F5-BFBB-93CD46D7564B` | Этот запрос требует передачу x-os-name |

---

## FastAuth/

Исключения QR-авторизации устройств. Сервис: [[Backend/FastAuth]]

| Файл | ErrorCode | ErrorMessage |
|------|-----------|--------------|
| `FastAuthInvalidConfirmationCodeException.cs` | `7B3F8E92-5D14-4C68-A7E2-1F9B6D3C8A45` | Неверный код подтверждения быстрой авторизации |
| `FastAuthInvalidStateException.cs` | `3C8A1E5F-7D29-4B83-9E16-5A2C8F4B7D31` | Сессия быстрой авторизации находится в недопустимом состоянии для этой операции |
| `FastAuthSessionExpiredException.cs` | `D2F71E8A-3C5B-4197-8A6D-4E9B27C5F1A8` | Сессия быстрой авторизации истекла |
| `FastAuthSessionNotFoundException.cs` | `A5E94C7D-1B82-4F36-9CDE-78B1F4A7E2C5` | Сессия быстрой авторизации не найдена |

---

## Files/

Исключения загрузки и работы с файлами. Сервис: [[Backend/Files]]

| Файл | ErrorCode | ErrorMessage |
|------|-----------|--------------|
| `FileNotFoundException.cs` | `91E25C73-FC80-43C1-893D-F26F39726F03` | Файл не найден |
| `NotValidFileIdException.cs` | `D10BD126-48EA-4D11-9CFF-4C2FDD6F9899` | Неверный формат идентификатора файла. Он должен быть guid |

---

## Messages/

Исключения чатов и сообщений. Сервис: [[Backend/Messages]]

| Файл | ErrorCode | ErrorMessage |
|------|-----------|--------------|
| `ChatIdNotValidException.cs` | `91CB4758-151F-4BF7-8C6D-435923CDF1AF` | Был передан невалидный chat id. Он должен быть guid |
| `ChatNotFoundException.cs` | `7506386A-8940-4F3B-87B8-315DD0A7AB08` | Чат не найден |
| `ChatNotPrivateException.cs` | `9F8E2C84-3B4D-4A7A-8F1C-5B2C0E77AC11` | Чат не является приватным |
| `DeviceIdRequiredException.cs` | `8E0F3D90-6F4B-4D4D-B5C8-3F6E1D2A0B43` | Операция требует device_id в токене |
| `EncryptedMessageNotFoundException.cs` | `FA3C1B6E-5D9F-4A48-AB12-DD3F62E3C481` | Шифрованное сообщение не найдено |
| `FileHasNotGroupPictureTypeException.cs` | `1ED8FF46-1CD7-4EFA-9CDD-8A07D18B2EE8` | Переданный файл имеет отличный тип от Изображение чата |
| `FileNotSupportedException.cs` | `DE755405-706A-4471-B3CA-5E0A3DCF8566` | Переданный файл не поддерживается для отправки в сообщении |
| `GroupChatTitleIsEmptyException.cs` | `0071F324-D75B-4AE9-93B4-BA62BD61AAEF` | *(ErrorMessage не переопределён)* |
| `GroupChatUsersIsEmptyException.cs` | `DB4FBD5D-30D5-44F6-B0AD-5AF13239523F` | *(ErrorMessage не переопределён)* |
| `InvalidEncryptedPayloadException.cs` | `9C82E2A7-5E2C-49EA-B12E-A1F70E64D3C7` | Некорректный шифрованный payload (ciphertext/nonce/AAD) |
| `IsNotGroupChatException.cs` | `DF5F9672-E0D6-4D6D-AC68-9D0B666ADD1E` | Это действие доступно только для групповых чатов |
| `MessageNotContainContextException.cs` | `C45A0486-AEBE-4A7F-B9BD-42BCEA4F843F` | Сообщение должно содержать хотя бы текст или вложения |
| `MessageNotFoundException.cs` | `C0EEF1D9-BE99-4645-9EBD-95FF36A2BF45` | Сообщение не найдено |
| `MessageTextTooLongException.cs` | `9F8B5C2A-7F1D-4E5A-9C3B-1F0E2D4A8B6C` | Текст сообщения превышает максимально допустимую длину (4096 символов) |
| `NoAccessToChatException.cs` | `604DD334-0484-4C6B-8113-354B9D2FDF2A` | Нет доступа к этому чату |
| `NoPermissionException.cs` | `AD582481-BAA9-4715-B3B9-825C886DFEC3` | У вас нет доступа для этого действия |
| `PrivateChatAlreadyAcceptedException.cs` | `5E2D3B6F-7C89-4D2A-8C71-44E6A12F0C20` | Приватный чат уже принят |
| `PrivateChatInviteNotFoundException.cs` | `7B19D8C2-1E2F-4F90-9F13-DCE8CC1A7F22` | Приглашение в приватный чат не найдено |
| `SecretInviteNotFoundException.cs` | `B12F7A45-3D8E-4B27-AE33-1C5E0F4F2A8E` | Инвайт секретного чата не найден или истёк |
| `SourceForSendMessageNotSetException.cs` | `2E70077F-D3C6-41A4-9D4D-49A0004CD54D` | Не указан chat_id или user_id для отправки сообщения |
| `TooManyAttachmentsException.cs` | `B3A4D7F2-5C6E-4A8B-9D1F-3E2C7B8A0F4D` | Превышено максимальное количество вложений в одном сообщении (10) |
| `TooManyPinnedMessagesException.cs` | `F7E1A4B8-2C9D-4F3A-B6E7-8D5C1A0F9B23` | Превышено максимальное количество закреплённых сообщений в чате (100) |
| `UserAlreadyMemberChatException.cs` | `7D3F0A52-8C41-4E96-9B0D-2E5F8A1C4B77` | Пользователь уже является участником этого чата |
| `UserNotMemberChatException.cs` | `CA1008EF-9487-4E37-A74A-C9B921F1D6CE` | Пользователь не является участником этого чата |

---

## Navigator/

Исключения регистрации серверов. Сервис: [[Backend/Navigator]]

| Файл | ErrorCode | ErrorMessage |
|------|-----------|--------------|
| `BeaconHostEmptyException.cs` | `8BD06066-81A5-43BF-84B6-A4112775E124` | BeaconHost не может быть пустым |
| `BeaconPortEmptyException.cs` | `F6E5D4C3-B2A1-4C5D-8B7A-9E0F1A2B3C4D` | BeaconPort не может быть пустым |
| `InvalidBeaconHostException.cs` | `B7C4D8E2-3F1A-4D6B-9C7E-2A8B5D6F1C3E` | BeaconHost имеет некорректный формат (ожидается hostname или IP-адрес) |
| `InvalidHexColorException.cs` | `E1F2A3B4-5C6D-4E7F-8A9B-0C1D2E3F4A5B` | Цвет имеет некорректный формат (ожидается #RRGGBB) |
| `NameEmptyException.cs` | `1A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D` | Name не может быть пустым |

---

## Users/

Исключения профилей пользователей. Сервис: [[Backend/Users]]

| Файл | ErrorCode | ErrorMessage |
|------|-----------|--------------|
| `BioTooLongException.cs` | `1A652492-87A4-4B8B-B758-E7FBE1F39DDF` | Био не должно превышать 200 символов |
| `ChatFolderInvalidNameException.cs` | `8C1A6F4D-1B22-4E1B-8E4D-7E9A5B6C2A11` | Название папки не должно быть пустым и не должно превышать 64 символа |
| `ChatFolderNotFoundException.cs` | `5F0B7B2E-3F6E-4D2B-9B9E-9B7E7C2B9D8A` | Папка чатов не найдена |
| `ProfilePictureHasNotValidType.cs` | `7097703F-977C-4E28-8C85-1A287B3FF8AD` | *(ErrorMessage не переопределён)* |
| `UserIsDraftException.cs` | `91D75288-8314-4658-AA0E-EC1D01779D58` | Пользователь с такими параметрами есть, но он не подтвержден |

---

## Замечания по качеству кода

- `GroupChatTitleIsEmptyException`, `GroupChatUsersIsEmptyException`, `ProfilePictureHasNotValidType` — **не переопределяют `ErrorMessage`**, используется базовое "Неизвестная ошибка"
- `InvalidRefreshTokenException` — переопределяет `ErrorMessage`, но возвращает `nameof(InvalidRefreshTokenException)` (буквально строку "InvalidRefreshTokenException") — по факту бесполезное сообщение, требует фикса
- `ConfirmationCodeExpiredException` — переопределяет и `ErrorCode`, и `ErrorMessage` корректно (ранее в этой доке было указано обратное — устарело)
- `ExceptionClientInterceptor.CachedExceptions` — статическое поле без `volatile`/`lock`, теоретически race condition при параллельной инициализации
