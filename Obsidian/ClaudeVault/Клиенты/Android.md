# BarkFluff.Client.Android

Kotlin + gRPC-OkHttp клиент. Activity-based архитектура.

Расположение: `Android/Barkfluff.Client.Android/`
Package: `com.barkfluff.client`

> Полная карта файлов и внутреннего строения: [[Android-ProjectMap]]

## Версии

- Kotlin 2.0.0, AGP 8.9.1
- gRPC-OkHttp 1.60.0 (NOT grpc-netty)
- ViewBinding, без Hilt/MVVM

## Архитектура

- Activity-based
- Локальное хранилище: SharedPreferences + EncryptedSharedPreferences для токенов
- Навигация: Welcome → SelectServer → Login → Chats

## UI — Экран списка чатов (MainActivity + ChatsFragment)

- `activity_main.xml`: `fragmentContainer` растянут на весь экран (`toBottomOf="parent"`), нижняя навигация (`bottomNavCard`) — плавающий элемент поверх, рисуется позже в XML → автоматически выше по z-order.
- `fragment_chats.xml`: `RecyclerView` (`chatRecyclerView`) занимает всё пространство фрагмента.
- `ChatAdapter` добавляет прозрачный **footer-спейсер** (126dp = 1.5 × высота элемента чата ≈ 84dp) в конец списка:
  - `VIEW_TYPE_FOOTER` / `FooterViewHolder` — не требует биндинга.
  - `submitList()` переопределён: `ensureFooter()` удаляет все footer-элементы из списка и добавляет один в конец перед каждой отправкой в DiffUtil.
  - Все методы изменения списка (`updateChatWithNewMessage`, `addNewChat`, `updateReadStatus`) фильтруют footer через `!it.isFooter`.
  - `ChatDiffCallback` корректно сравнивает footer-элементы.
  - Layout: `res/layout/item_chat_footer.xml` — прозрачная `View` высотой 126dp.

- grpc-okhttp 1.60.0 (coroutine stubs)
- `MetadataUtils.attachHeaders` не резолвится в grpc-okhttp 1.60.0 — использовать `ClientInterceptor` напрямую

## Хранение токенов

- EncryptedSharedPreferences (`barkfluff_secure_prefs`)
- При создании gRPC-каналов добавлять `x-auth-token` через interceptor

## Вложения и медиа

- `MessageAttachmentType`: Unknown, Image, Video, Gif, Document, Audio(4), Voice, Sticker; **AUDIO=5** (добавлен в shared.proto)
- `FileCache` (`utils/FileCache.kt`) — singleton disk cache, путь: `cacheDir/media_files/`
- `AudioPlayerHelper` (`utils/AudioPlayerHelper.kt`) — MediaPlayer singleton, один аудио за раз
- `ImageGridAdapter` (`adapter/ImageGridAdapter.kt`) — квадратная сетка с `SquareImageView`
- `SquareImageView` (`views/SquareImageView.kt`) — `onMeasure` устанавливает height=width
- `MediaViewerActivity` — ExoPlayer, не fullscreen, swipe-down dismiss
- `ImageViewerActivity` — без fullscreen, swipe-down dismiss
- `ChatRepository.downloadFile()` — HTTP download → FileCache

## ExoPlayer

```
androidx.media3:media3-exoplayer:1.3.1
androidx.media3:media3-ui:1.3.1
```

## gRPC Коды ошибок (из x-error-code trailer)

| Исключение | ErrorCode |
|-----------|-----------|
| OtpCodeNeedException | `C1576884-12D8-4722-A7EE-9F9789AD1265` |
| NotValidOtpCodeException | `803B632C-4457-4B05-9435-9C3DD0F41E00` |
| InvalidLoginOrPasswordException | `21BFB9B5-C377-45D1-9B15-6B7F3432B397` |

## Файловая структура

- `gradle/libs.versions.toml` — все версии зависимостей
- `app/src/main/proto/` — 11 proto файлов
- `app/src/main/java/com/barkfluff/client/` — все исходники
- `PROJECT_MAP.md` — полная карта Android-проекта

## Сборка

```bash
cd Android/Barkfluff.Client.Android
./gradlew assembleDebug
```
