# Система кеширования файлов в Android-клиенте BarkFluff

> Документ описывает всю систему кеширования серверных файлов (изображения, видео, аудио, документы, аватары, стикеры) в приложении `Barkfluff.Client.Android`. Предназначен как справочный материал для реализации аналогичной системы на iOS.

---

## Общая концепция

Сервер хранит файлы в объектном хранилище (Minio/S3) и не раздаёт их напрямую через gRPC. Клиент работает с двумя видами ссылок:

- **fileId** — стабильный идентификатор файла на сервере (UUID-подобная строка).
- **downloadUrl** — временный HTTP(S)-URL для скачивания файла, получается через gRPC-запрос `getFileDownloadUrl(fileId)` к Files API.

Ключевая задача системы кеширования — избежать лишних gRPC-запросов за URL и избежать повторного скачивания уже загруженных байтов файлов.

---

## Архитектура: четыре слоя кеша

```
┌──────────────────────────────────────────────────────────┐
│                      ЗАПРОС ФАЙЛА                        │
│   (по fileId через AvatarLoader / ImageLoadHelper)       │
└───────────────────────────┬──────────────────────────────┘
                            │
             ┌──────────────▼───────────────┐
             │  1. Runtime URL-кэш          │  ConcurrentHashMap<fileId, URL>
             │  AvatarLoader.urlCache        │  только в памяти, живёт пока жив процесс
             └──────────────┬───────────────┘
                            │ miss
             ┌──────────────▼───────────────┐
             │  2. Persistent URL-кэш        │  FileUrlCache → SharedPreferences
             │  FileUrlCache.getUrl()        │  fileId хешируется SHA-256, выживает перезапуск
             └──────────────┬───────────────┘
                            │ miss
             ┌──────────────▼───────────────┐
             │  3. gRPC-запрос URL           │  getFileDownloadUrl(fileId) → Files API
             │  GrpcManager                  │  результат сохраняется в слои 1 и 2
             └──────────────┬───────────────┘
                            │ URL получен
             ┌──────────────▼───────────────┐
             │  4. Coil Image Cache          │  memory (25% RAM) + disk (10% storage)
             │  AvatarLoader.getImageLoader  │  ключ кеша = fileId (не URL)
             └──────────────────────────────┘
```

Слои 1–3 кешируют **URL** (маппинг fileId → downloadUrl).  
Слой 4 (Coil) кешируют **пиксели / байты изображений** по этому URL.

---

## Компоненты системы

### 1. `FileUrlCache` — персистентный URL-кэш

**Файл:** `utils/FileUrlCache.kt`  
**Тип:** Singleton (двойная проверка блокировки)  
**Хранилище:** `SharedPreferences` с именем `"file_url_cache"`

Хранит маппинг `fileId → downloadUrl` на диске — чтобы при следующем запуске приложения не ходить за URL повторно через gRPC.

| Метод | Описание |
|---|---|
| `initialize()` | загружает все записи из SharedPreferences в in-memory `Map` |
| `getUrl(fileId)` | возвращает закешированный URL или null |
| `putUrl(fileId, url)` | сохраняет в память и на диск |
| `clear()` | очищает всё (вызывается из настроек хранилища) |

**Особенности:**
- Ключ хешируется через SHA-256 перед сохранением в SharedPreferences (безопасные имена ключей).
- Все операции синхронизированы через `synchronized(this)`.
- `initialize()` и `putUrl()` выполняются в `Dispatchers.IO`.
- Получение через `getUrl()` синхронно (из памяти, после `initialize()`).

---

### 2. `AvatarLoader` — загрузчик аватаров и точка входа в URL-кеш

**Файл:** `utils/AvatarLoader.kt`  
**Тип:** Kotlin `object` (singleton)

Центральный компонент для загрузки аватаров пользователей и чатов. Реализует трёхуровневую стратегию получения URL и передаёт URL в Coil.

#### Runtime URL-кэш (слой 1)

```kotlin
internal val urlCache = ConcurrentHashMap<String, String>()
```

Живёт только в памяти (нет персистентности). Наполняется при:
- промоутировании из `FileUrlCache` (слой 2) после hit
- получении URL через gRPC (слой 3)

#### Coil ImageLoader (слой 4)

Создаётся лениво (double-checked locking):

```kotlin
ImageLoader.Builder(context)
    .okHttpClient(okHttpClient)           // trust-all SSL
    .memoryCache { maxSizePercent(0.25) } // 25% RAM
    .diskCache {
        directory(cacheDir / "image_cache")
        maxSizePercent(0.10)              // 10% internal storage
    }
    .respectCacheHeaders(false)           // игнорирует Cache-Control сервера
    .build()
```

Диск-кэш Coil находится в `cacheDir/image_cache/`.  
`respectCacheHeaders(false)` — сервер не управляет временем жизни кеша на клиенте.

#### Ключевые методы

| Метод | Описание |
|---|---|
| `initializeCache(context)` | вызывается из `BarkFluffApplication.onCreate()`, создаёт `FileUrlCache` и запускает `initialize()` |
| `loadByFileId(imageView, placeholderView, fileId, …, getUrlCallback)` | основной метод загрузки аватара по fileId, трёхуровневая стратегия |
| `load(imageView, placeholderView, avatarUrl, …)` | загрузка по готовому URL напрямую через Coil |
| `loadIntoImageView(imageView, avatarUrl, …)` | упрощённый вариант без отдельного placeholder |
| `getUrlFromCache(fileId)` | читает из `FileUrlCache` |
| `putUrlInCache(fileId, url)` | записывает в `FileUrlCache` |
| `clearAllCaches(context)` | очищает memory и disk кэш Coil + runtime urlCache + FileUrlCache |

#### Алгоритм `loadByFileId`

```
1. Если fileId начинается с "http" → загрузить напрямую через Coil (это URL)
2. Проверить urlCache (ConcurrentHashMap) — runtime hit
3. Проверить FileUrlCache (SharedPreferences) — persistent hit, записать в urlCache
4. Cache miss → показать placeholder с инициалами → запустить корутину:
   a. Вызвать getUrlCallback() (gRPC: getFileDownloadUrl)
   b. Записать URL в urlCache и FileUrlCache
   c. Запустить loadImageWithCoil
```

**Защита от race condition при RecyclerView recycling:**  
`imageView.tag = fileId` перед запросом, проверка `imageView.tag != fileId` перед отрисовкой результата.

#### Coil-запрос для аватара

```kotlin
ImageRequest.Builder(context)
    .data(url)
    .memoryCacheKey(fileId)   // ключ кеша = fileId, не URL
    .diskCacheKey(fileId)
    .crossfade(200)
    .transformations(CircleCropTransformation())
    .build()
```

Использование `fileId` как cache key (не URL) позволяет переиспользовать кешированное изображение, даже если URL изменился (временные ссылки).

---

### 3. `ImageLoadHelper` — загрузка изображений-вложений

**Файл:** `utils/ImageLoadHelper.kt`  
**Тип:** Kotlin `object`

Аналог `AvatarLoader.loadByFileId`, но **без** `CircleCropTransformation`. Используется для preview и полноэкранного просмотра изображений в сообщениях.

Полностью переиспользует инфраструктуру `AvatarLoader`:
- читает/пишет `AvatarLoader.urlCache`
- читает/пишет через `AvatarLoader.getUrlFromCache` / `putUrlInCache`
- использует `AvatarLoader.getImageLoader(context)` для Coil-запроса
- устанавливает `memoryCacheKey(fileId)` и `diskCacheKey(fileId)`

Параметр `size: Int` — если `> 0`, ограничивает разрешение загружаемого изображения (для thumbnail).

---

### 4. `FileCache` — дисковый кэш бинарных файлов

**Файл:** `utils/FileCache.kt`  
**Тип:** Kotlin `object` (singleton)  
**Хранилище:** `cacheDir/media_files/`

Используется для **не-изображений**: аудио, видео, документы. Также для изображений, которые нужно сохранить в Downloads.

| Метод | Описание |
|---|---|
| `init(context)` | создаёт директорию `cacheDir/media_files/` |
| `hasFile(fileId)` | проверяет наличие файла |
| `getFile(fileId)` | возвращает `File?` или null |
| `saveFile(fileId, bytes)` | сохраняет `ByteArray` |
| `saveFile(fileId, stream)` | сохраняет из `InputStream` |
| `deleteFile(fileId)` | удаляет файл из кеша |

Имя файла = `fileId` после санитизации (все символы кроме `[a-zA-Z0-9_\-]` заменяются на `_`).

**Жизненный цикл файлов в `FileCache`:**
- Файл помещается в кеш при вызове `ChatRepository.downloadFile()`.
- Файл можно удалить через контекстное меню в `MessageAdapter` (аудио, документы).
- Весь кеш живёт в системном `cacheDir` — Android может очистить его при нехватке места.

---

### 5. `ImageCache` — LRU bitmap-кэш (вспомогательный)

**Файл:** `utils/ImageCache.kt`  
**Тип:** Singleton  
**Хранилище:** memory (LRU) + `cacheDir/bitmap_cache/`

Двухуровневый кэш для сырых `Bitmap`-объектов. Размер memory cache = 1/8 доступной Java heap.

**Статус в проекте:** Класс реализован, но активно не используется в основном flow (загрузка через Coil покрывает большинство сценариев). Учитывается при расчёте размера кеша в `StorageSettingsActivity` (директория `bitmap_cache`).

---

### 6. `StickerCache` — кэш данных стикер-панели

**Файл:** `utils/StickerCache.kt`  
**Тип:** Kotlin `object` (singleton)  
**Хранилище:** `SharedPreferences` с именем `"sticker_cache"`

Кеширует **список** `StickerPanelItem` (метаданные паков и стикеров, включая `fileUrl` и `previewUrl`) в JSON.

| Метод | Описание |
|---|---|
| `init(context)` | инициализация SharedPreferences |
| `savePanelData(items)` | сериализует список в JSON, сохраняет |
| `loadPanelData()` | десериализует JSON, возвращает список или null |
| `clear()` | удаляет ключ из SharedPreferences |

**Применение:** `ChatActivity.loadStickerPanelData()` — сначала пробует загрузить из `StickerCache`, если пусто — запрашивает с сервера и сохраняет результат.

Изображения самих стикеров кешируются Coil через обычный URL (из поля `fileUrl` / `previewUrl` в ответе сервера).

---

## Инициализация при старте приложения

Все кеши инициализируются в `BarkFluffApplication.onCreate()`:

```kotlin
// 1. Персистентный URL-кэш (FileUrlCache через AvatarLoader)
AvatarLoader.initializeCache(this)   // → FileUrlCache.getInstance().initialize()

// 2. Дисковый кэш бинарных файлов
FileCache.init(this)                  // → создаёт cacheDir/media_files/

// 3. Кэш стикер-панели
StickerCache.init(this)               // → получает SharedPreferences
```

`ImageCache` не инициализируется явно (lazy через `getInstance()`).  
`Coil ImageLoader` не инициализируется явно (lazy в `AvatarLoader.getImageLoader()`).

---

## Получение URL файла: `GrpcManager.getFileDownloadUrl`

Источник истины для URL — метод `GrpcManager.getFileDownloadUrl(fileId)`. Он делает gRPC-запрос к **Files API** (`getFileDownloadUrl`) и возвращает временный HTTP(S)-URL.

Этот URL передаётся через `getFileUrl: suspend (String) -> String?` lambda — callback, который `ChatActivity` передаёт в `MessageAdapter` и `StickerPanelAdapter`:

```kotlin
getFileUrl = { fileId -> chatRepository.getFileDownloadUrl(fileId).getOrNull() }
```

---

## Скачивание файлов в FileCache: `ChatRepository.downloadFile`

```kotlin
suspend fun downloadFile(fileId: String, onProgress: (Int) -> Unit): File?
```

1. Получает `downloadUrl` через `getFileDownloadUrl(fileId)`.
2. Скачивает файл через `HttpURLConnection` (с trust-all SSL для самоподписанных сертификатов).
3. Сохраняет байты в `FileCache.saveFile(fileId, bytes)`.
4. Возвращает `File`.

`onProgress` — callback 0..100 для отображения прогресса в UI.

---

## Где и как используется кеш

### `ChatActivity`

| Ситуация | Компонент |
|---|---|
| Аватар чата / собеседника в шапке | `AvatarLoader.loadByFileId(...)` → gRPC → Coil |
| Аватар в диалоге профиля чата | `AvatarLoader.loadByFileId(...)` → gRPC → Coil |
| Превью вложения при его открытии (Image) | `AvatarLoader.getImageLoader` + Coil напрямую |
| Загрузка стикер-панели | `StickerCache.loadPanelData()` → gRPC → `StickerCache.savePanelData()` |
| Открытие видео | `FileCache.getFile(fileId)` (кеш диск) или скачать |
| Открытие аудио/документа | `FileCache.getFile(fileId)` → `chatRepository.downloadFile()` |

### `MessageAdapter`

| Тип вложения | Компонент |
|---|---|
| Изображение (preview) | `ImageLoadHelper.loadByFileId` → URL-кеш + Coil |
| Аватар отправителя (групповой чат) | `getFileUrl()` → Coil `.load(url)` |
| Видео thumbnail | `ImageLoadHelper.loadByFileId` |
| Видео файл | `FileCache.hasFile/getFile` + `downloadToCache()` |
| Аудио файл | `FileCache.hasFile/getFile/deleteFile` + `downloadToCache()` |
| Документ | `FileCache.hasFile/getFile/deleteFile` + `downloadToCache()` |

### `ChatsFragment` / `ContactsFragment`

Аватары пользователей и чатов: `AvatarLoader.loadByFileId(...)`.

### `StickerPanelAdapter`

Изображения стикеров: через Coil `imageView.load(url)` (Coil сам кеширует).

### `ImageViewerActivity`

Полноразмерные изображения: `ImagePagerAdapter` использует `chatRepository.getFileDownloadUrl()` + Coil.

---

## Очистка кеша (`StorageSettingsActivity`)

Пользователь может очистить кеш через Настройки → Хранилище.

```kotlin
// Очищает memory cache Coil, disk cache Coil (image_cache/),
// runtime urlCache (ConcurrentHashMap), персистентный FileUrlCache
AvatarLoader.clearAllCaches(context)

// Дополнительно удаляет bitmap_cache/
File(cacheDir, "bitmap_cache").deleteRecursively()
```

**Что НЕ очищается этим методом:**
- `FileCache` (`cacheDir/media_files/`) — не затрагивается кнопкой "Очистить кеш" через этот путь.
- `StickerCache` (SharedPreferences) — отдельный механизм.

Размер кеша считается по директориям `image_cache/` и `bitmap_cache/`.

---

## Директории на диске

| Путь | Содержимое | Управляется |
|---|---|---|
| `cacheDir/image_cache/` | Закодированные изображения (Coil disk cache) | Coil, `AvatarLoader.clearAllCaches()` |
| `cacheDir/bitmap_cache/` | Сырые PNG-битмапы (ImageCache) | `ImageCache`, `StorageSettingsActivity` |
| `cacheDir/media_files/` | Аудио, видео, документы | `FileCache` |
| SharedPreferences `file_url_cache` | fileId → URL маппинг (JSON) | `FileUrlCache`, `AvatarLoader.clearAllCaches()` |
| SharedPreferences `sticker_cache` | JSON панели стикеров | `StickerCache` |

Все пути лежат внутри `cacheDir` (внутреннее хранилище приложения, недоступно другим приложениям). Android может автоматически очищать `cacheDir` при нехватке места.

---

## Итоговая схема потока данных

```
Экран запрашивает изображение по fileId
         │
         ▼
AvatarLoader / ImageLoadHelper
         │
         ├─► urlCache hit → Coil.enqueue(url, memoryCacheKey=fileId)
         │                          │
         │                          ├─► memory cache hit → ImageView ✓
         │                          └─► disk cache hit  → ImageView ✓
         │                                  │ miss
         │                                  └─► HTTP GET url → decode → cache → ImageView ✓
         │
         ├─► FileUrlCache hit → populate urlCache → Coil.enqueue(...)
         │
         └─► full miss → gRPC getFileDownloadUrl(fileId)
                          │
                          └─► populate urlCache + FileUrlCache → Coil.enqueue(...)

Экран запрашивает бинарный файл (аудио/видео/doc) по fileId
         │
         ▼
FileCache.hasFile(fileId)?
         │
         ├─► YES → File готов к воспроизведению/открытию
         │
         └─► NO → ChatRepository.downloadFile(fileId, onProgress)
                          │
                          └─► gRPC getFileDownloadUrl → HTTP GET → FileCache.saveFile → File ✓
```

---

## Ключевые архитектурные решения для iOS

При реализации на iOS рекомендуется воспроизвести следующие концепции:

1. **Разделение URL-кеша и байт-кеша.** fileId → URL — отдельный персистентный слой (аналог `FileUrlCache`). Байты изображений — отдельный слой (аналог Coil).

2. **fileId как cache key для изображений.** Использовать `fileId` (не URL) как ключ в image-кеше. URL может меняться (временные ссылки), fileId — стабилен.

3. **Трёхуровневый lookup:** runtime in-memory map → persistent store → gRPC request.

4. **Отдельный дисковый кеш для бинарных файлов** (аудио, видео, документы) — аналог `FileCache`. Хранить в `Library/Caches/<bundle_id>/media_files/`.

5. **Ignore server cache headers.** Временные S3-URL имеют короткое время жизни в Cache-Control. На клиенте нужно игнорировать эти заголовки и управлять временем жизни кеша самостоятельно.

6. **Защита от race condition при reuse ячеек** (UITableView/UICollectionView): привязывать fileId к ячейке и проверять перед установкой изображения.

7. **Очистка кеша** из Settings → Storage: должна затрагивать все слои (image memory, image disk, URL persistent store).
