# Аудит проекта: Barkfluff.Client.Android

> **Дата:** <!-- заполнить при обновлении -->
> **Ветка:** `dev`
> **Проверенные файлы:** все `.kt` из `app/src/main/java/com/barkfluff/client/`

---

## Содержание

1. [🔴 Безопасность](#безопасность)
2. [🟡 Оптимизация](#оптимизация)
3. [🟠 Баги и недоработки](#баги-и-недоработки)
4. [🔵 Прочее / Технический долг](#прочее--технический-долг)

---

## 🔴 Безопасность

---

### SEC-01 — Отключена проверка SSL-сертификата (Trust All)

**Проблема / Описание**
В нескольких местах используется кастомный `X509TrustManager`, который не выполняет никакой проверки — принимает любой TLS-сертификат, в том числе поддельный. Это открывает возможность для **Man-in-the-Middle (MITM)** атаки: злоумышленник может перехватить весь трафик, включая токены авторизации и содержимое сообщений.

**Конкретно в чём проблема**
Методы `checkClientTrusted` и `checkServerTrusted` пустые, `getAcceptedIssuers` возвращает пустой массив. Также установлен `hostnameVerifier { _, _ -> true }`, который принимает любое имя хоста.

| Файл | Строки |
|------|--------|
| `grpc/GrpcManager.kt` | 737–744 (createChannel) |
| `grpc/GrpcManager.kt` | 1487–1495 (uploadAvatar) |
| `grpc/GrpcManager.kt` | 2298–2306 (uploadProfilePoster) |
| `repository/ChatRepository.kt` | 236–244 (uploadFile) |
| `utils/AvatarLoader.kt` | 53–69 (OkHttpClient) |

**Снипет — проблемный код:**
```kotlin
// GrpcManager.kt:737 — createChannel
// ❌ Любой сертификат принимается, MITM возможен
val trustManager = object : X509TrustManager {
    override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {} // пусто — нет проверки
    override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {} // пусто — нет проверки
    override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()               // нет доверенных CA
}
val sslContext = SSLContext.getInstance("TLS")
sslContext.init(null, arrayOf<TrustManager>(trustManager), null)
builder.sslSocketFactory(sslContext.socketFactory)
// ... (аналогично в AvatarLoader.kt:65)
connection.hostnameVerifier = javax.net.ssl.HostnameVerifier { _, _ -> true } // ❌ любой хост принят
```

**Варианты решения**

**Вариант А (рекомендуемый для production):** использовать системный TrustManager — доверять только сертификатам из системного CA store. Для dev-серверов добавить self-signed CA в `res/xml/network_security_config.xml`.

```xml
<!-- res/xml/network_security_config.xml -->
<network-security-config>
    <debug-overrides>
        <!-- Только для DEBUG сборок: доверяем самоподписанному CA сервера разработки -->
        <trust-anchors>
            <certificates src="@raw/dev_server_ca"/> <!-- файл res/raw/dev_server_ca.crt -->
            <certificates src="system"/>
        </trust-anchors>
    </debug-overrides>
    <base-config cleartextTrafficPermitted="false">
        <trust-anchors>
            <certificates src="system"/> <!-- только системные CA в release -->
        </trust-anchors>
    </base-config>
</network-security-config>
```

```kotlin
// GrpcManager.kt — createChannel() — исправленная версия
private fun createChannel(address: String): ManagedChannel {
    val url = ensureHttpPrefix(address)
    val useTls = url.startsWith("https://")
    val hostPort = url.removePrefix("http://").removePrefix("https://")
    val parts = hostPort.split(":")
    val host = parts[0]
    val port = parts[1].toInt()

    val builder = OkHttpChannelBuilder.forAddress(host, port)
    if (useTls) {
        // ✅ Используем системный TrustManager — сертификаты проверяются по системному CA store
        // Self-signed CA для dev добавляется через network_security_config.xml
        builder.useTransportSecurity() // TLS включён, проверка через систему
    } else {
        builder.usePlaintext()
    }
    return builder.build()
}
```

```kotlin
// AvatarLoader.kt — исправленная версия okHttpClient
private val okHttpClient by lazy {
    // ✅ Без кастомного TrustManager — OkHttp использует системные CA
    OkHttpClient.Builder()
        .connectTimeout(30, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .build()
}
```

---

### SEC-02 — Пароль хранится в поле Activity в открытом виде

**Проблема / Описание**
В `LoginActivity` пароль пользователя сохраняется в поле `savedPassword: String` для последующего повторного использования при 2FA. Строки в JVM — неизменяемые объекты в heap. GC может не очистить их немедленно. При memory dump или отладке пароль виден в памяти процесса.

**Конкретно в чём проблема**
Поле `savedPassword` живёт весь жизненный цикл Activity (до `onDestroy`). Если OTP-сессия долгая — пароль в памяти долго.

**Путь к файлу:** `LoginActivity.kt` : 44–45, 163–164, 203

```kotlin
// LoginActivity.kt:44
// ❌ Пароль хранится как обычная String (immutable, GC не гарантирует очистку)
private var savedPassword = ""
// ...
savedPassword = password     // строка 164
// ...
password = savedPassword,    // строка 203 — используется при OTP-подтверждении
```

**Варианты решения**

Использовать `CharArray` вместо `String` — массив можно явно обнулить после использования.

```kotlin
// ✅ Исправленная версия LoginActivity
private var savedPasswordChars: CharArray = charArrayOf()

// При сохранении:
savedPasswordChars = password.toCharArray()

// При использовании:
val passwordStr = String(savedPasswordChars)
grpcManager.auth(password = passwordStr, ...)

// После успешного логина или ошибки — явная очистка:
savedPasswordChars.fill('\u0000') // обнуляем байты в памяти
savedPasswordChars = charArrayOf()
```

---

### SEC-03 — Внешний IP-адрес кешируется и не обновляется

**Проблема / Описание**
`GlobalParam.loadIpAddress` загружает внешний IP только если он ещё не сохранён (`if (currentIp.isBlank())`). Если пользователь сменил сеть (Wi-Fi → мобильный, VPN и т.д.), IP в SharedPreferences останется старым и будет отправляться на сервер как метаданные устройства — неточные данные для аудита сессий.

**Путь к файлу:** `data/GlobalParam.kt` : 345–354

```kotlin
// GlobalParam.kt:346
// ❌ IP обновляется только один раз за всё время жизни установки
suspend fun loadIpAddress(sharedPreferences: SharedPreferences) {
    val currentIp = sharedPreferences.getString(KEY_IP_ADDRESS, "") ?: ""
    if (currentIp.isBlank()) { // ← обновляем только если пусто
        val externalIp = NetworkUtils.getExternalIp()
        if (externalIp.isNotBlank()) {
            sharedPreferences.edit().putString(KEY_IP_ADDRESS, externalIp).apply()
        }
    }
    // Если IP уже сохранен — используем его (не обновляем лишний раз)
}
```

**Вариант решения** — всегда обновлять IP при старте активности:

```kotlin
// ✅ Всегда обновляем — стоимость: один HTTP-запрос на старт
suspend fun loadIpAddress(sharedPreferences: SharedPreferences) {
    val externalIp = NetworkUtils.getExternalIp()
    if (externalIp.isNotBlank()) {
        sharedPreferences.edit().putString(KEY_IP_ADDRESS, externalIp).apply()
    }
    // Если не удалось получить — оставляем предыдущий (graceful fallback)
}
```

---

## 🟡 Оптимизация

---

### OPT-01 — Каждый вызов `GlobalParam(context)` создаёт новый экземпляр и открывает SharedPreferences

**Проблема / Описание**
`GlobalParam` — не singleton и не scoped-объект. В `GrpcManager` он создаётся внутри методов (`auth`, `ensureTokenValid`, `forceRefreshToken`, `refreshTokenInternal`) при каждом вызове. Каждое создание `GlobalParam` инициализирует два `SharedPreferences` через `lazy` — это обращение к файловой системе на первом доступе. При активном трафике (обновление токена при реконнекте) это порождает лишние объекты и потенциальные аллокации.

**Путь к файлу:** `grpc/GrpcManager.kt` : 307, 413, 435

```kotlin
// GrpcManager.kt:307 — создаётся на каждый вызов auth()
// GrpcManager.kt:413 — создаётся на каждый вызов ensureTokenValid()
// GrpcManager.kt:435 — создаётся на каждый вызов forceRefreshToken()

// ❌ Новый экземпляр при каждом вызове
val globalParam = GlobalParam(context)
```

**Вариант решения** — передавать `GlobalParam` снаружи (уже принято в некоторых местах), либо кешировать в методах, которые вызываются часто:

```kotlin
// ✅ В GrpcManager — хранить глобальный экземпляр после первой инициализации
private var cachedGlobalParam: GlobalParam? = null

private fun getOrCreateGlobalParam(context: Context): GlobalParam {
    return cachedGlobalParam ?: GlobalParam(context.applicationContext).also {
        cachedGlobalParam = it
    }
}

// В auth(), ensureTokenValid(), forceRefreshToken() и т.д.:
val globalParam = getOrCreateGlobalParam(context)
```

---

### OPT-02 — `CoroutineScope` создаётся внутри `setupMessagesRecyclerView` и не отменяется

**Проблема / Описание**
В `ChatActivity.setupMessagesRecyclerView()` создаётся `CoroutineScope(Dispatchers.Main)` и передаётся в `MessageAdapter`. Этот scope **не привязан к lifecycle Activity** и не отменяется при `onDestroy`. Все запущенные из адаптера корутины (загрузка файлов, аватаров, медиа) продолжают работать после уничтожения Activity — это утечка памяти и потенциальные краши (работа с уже уничтоженными View).

**Путь к файлу:** `ChatActivity.kt` : 393

```kotlin
// ChatActivity.kt:393
// ❌ Scope не привязан к lifecycle, не будет отменён при onDestroy()
val scope = kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.Dispatchers.Main)
messageAdapter = MessageAdapter(
    ...
    scope = scope,
    ...
)
```

**Вариант решения** — использовать `lifecycleScope`:

```kotlin
// ✅ lifecycleScope автоматически отменяется при onDestroy() Activity
messageAdapter = MessageAdapter(
    ...
    scope = lifecycleScope, // привязан к жизненному циклу Activity
    ...
)
```

---

### OPT-03 — `FileCache.saveFile(ByteArray)` читает весь файл в память

**Проблема / Описание**
При кешировании фона чата скачивается URL, весь поток читается через `readBytes()` в `ByteArray`, и затем записывается на диск. Для больших изображений фона (1–5 МБ) это двойное расходование памяти: сначала буфер в heap, потом запись на диск. На устройствах с ограниченной RAM может вызвать GC pressure.

**Путь к файлу:** `ChatActivity.kt` : 326–332

```kotlin
// ChatActivity.kt:326
// ❌ Весь файл загружается в ByteArray — двойное потребление памяти
val connection = java.net.URL(url).openConnection() as java.net.HttpURLConnection
connection.connect()
val bytes = connection.inputStream.readBytes() // ← весь файл в heap
connection.disconnect()
FileCache.saveFile(fileId, bytes)
```

**Вариант решения** — использовать перегрузку `FileCache.saveFile(fileId, InputStream)`, которая уже есть:

```kotlin
// ✅ Стриминг — не загружаем весь файл в память
val connection = java.net.URL(url).openConnection() as java.net.HttpURLConnection
connection.connect()
try {
    // FileCache.saveFile(String, InputStream) уже реализован — используем его
    FileCache.saveFile(fileId, connection.inputStream, connection.contentLengthLong)
} finally {
    connection.disconnect()
}
```

---

### OPT-04 — `FileUrlCache` логирует каждый hit/miss в SharedPreferences через `Log.d`

**Проблема / Описание**
`FileUrlCache.getUrl()` вызывает `Log.d` на каждое обращение к кешу URL. В списке чатов с десятками аватаров это десятки лог-вызовов на каждый рендер. `Log.d` в Android выполняется синхронно и хотя нередко компилируется в no-op в release-сборках, в debug-режиме это лишний IO в главном потоке (если вызывается из UI thread через `AvatarLoader`).

**Путь к файлу:** `utils/FileUrlCache.kt` : 48–51

```kotlin
// FileUrlCache.kt:48
// ❌ Log.d на каждый cache hit/miss — избыточно при большом количестве аватаров
fun getUrl(fileId: String): String? {
    synchronized(this@FileUrlCache) {
        val url = cache[hashKey(fileId)]
        if (url != null) {
            Log.d(TAG, "Cache hit for fileId=$fileId")  // ← на каждый запрос
        } else {
            Log.d(TAG, "Cache miss for fileId=$fileId") // ← на каждый промах
        }
        return url
    }
}
```

**Вариант решения** — убрать логирование из горячего пути, оставить только счётчики или редкое сводное логирование:

```kotlin
// ✅ Без лишних логов в горячем пути
private var hitCount = 0
private var missCount = 0

fun getUrl(fileId: String): String? {
    synchronized(this@FileUrlCache) {
        val url = cache[hashKey(fileId)]
        if (url != null) hitCount++ else missCount++
        // Редкое сводное логирование (каждые 100 запросов)
        if ((hitCount + missCount) % 100 == 0) {
            Log.v(TAG, "Cache stats: hits=$hitCount, misses=$missCount")
        }
        return url
    }
}
```

---

### OPT-05 — `AudioPlayerHelper` — singleton MediaPlayer без освобождения при уходе в фон

**Проблема / Описание**
`AudioPlayerHelper` — глобальный `object`, хранит `MediaPlayer`. При сворачивании приложения (`onStop` в Activity/Fragment) `release()` не вызывается автоматически. MediaPlayer удерживает аудио-фокус и буфер декодера. На некоторых устройствах это приводит к тому, что аудио продолжает воспроизводиться в фоне или не отпускает аудио-фокус для других приложений (звонки, музыка).

**Путь к файлу:** `utils/AudioPlayerHelper.kt` : 13–141

```kotlin
// AudioPlayerHelper.kt — объект не знает о lifecycle
// ❌ Нет подписки на onStop/onPause Activity — mediaPlayer жив в фоне
object AudioPlayerHelper {
    private var mediaPlayer: MediaPlayer? = null
    // ...
    // release() вызывается только явно — нет автоматической остановки при уходе в фон
}
```

**Вариант решения** — в `ChatActivity.onStop()` вызывать `pause()` явно, а в `onDestroy()` — `release()`:

```kotlin
// ChatActivity.kt — добавить:
override fun onStop() {
    super.onStop()
    // ✅ Останавливаем воспроизведение при уходе в фон (аудио-фокус освобождается)
    AudioPlayerHelper.pause()
}

override fun onDestroy() {
    super.onDestroy()
    // ✅ Полное освобождение ресурсов MediaPlayer при уничтожении чата
    AudioPlayerHelper.release()
}
```

---

### OPT-06 — `recreateAllClients` вызывается при каждом `resume()` — пересоздаёт все каналы без проверки

**Проблема / Описание**
В `RealtimeService.resume()` всегда вызывается `grpcManager.recreateAllClients(context, globalParam)`. Это закрывает все старые gRPC-каналы и создаёт новые. При быстром сворачивании/разворачивании приложения (например, переключение между приложениями) это порождает N пересозданий каналов подряд — каждый раз устанавливаются новые TCP-соединения. На медленном канале это заметная задержка до появления новых сообщений.

**Путь к файлу:** `grpc/RealtimeService.kt` : 93–94

```kotlin
// RealtimeService.kt:93
// ❌ Безусловное пересоздание всех каналов при каждом resume
fun resume() {
    // ...
    grpcManager.recreateAllClients(context, globalParam) // всегда, даже если каналы живые
    // ...
}
```

**Вариант решения** — пересоздавать только при реальном возврате из фона (флаг `cameFromBackground` уже есть в `BarkFluffApplication`):

```kotlin
// ✅ Пересоздаём каналы только при возврате из фона, не при каждом resume
fun resume() {
    val currentScope = serviceScope
    if (currentScope != null && currentScope.isActive) return

    val app = context.applicationContext as BarkFluffApplication
    if (app.cameFromBackground) {
        // Каналы могли сломаться за время фона — пересоздаём
        grpcManager.recreateAllClients(context, globalParam)
        app.cameFromBackground = false
    }
    // иначе — просто запускаем новый scope со старыми каналами
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    serviceScope = scope
    // ... запуск стримов
}
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — `SplashActivity` создаёт собственный `GrpcManager`, который конкурирует с app-level

**Проблема / Описание**
`SplashActivity` создаёт `grpcManager = GrpcManager()` — отдельный экземпляр, независимый от `BarkFluffApplication.grpcManager`. После `tryRefreshToken()` и `loadUserDataAndNavigateToChats()` этот экземпляр принудительно shutdown-ится (`grpcManager.shutdown()`). Но при переходе в `navigateToChats()` используется `app.grpcManager.recreateAllClients(...)`. Если refresh-токен успешно обновлён, новые токены сохраняются в `GlobalParam`, и app-level `grpcManager` подхватит их — это работает. Однако если `tryRefreshToken()` вызывает `grpcManager.shutdown()` в блоке `finally`, а потом продолжается выполнение `loadUserDataAndNavigateToChats()` (который тоже использует тот же `grpcManager`), возможна ситуация использования shutdown-каналов.

**Путь к файлу:** `SplashActivity.kt` : 34, 143, 201

```kotlin
// SplashActivity.kt:34
// ❌ Отдельный экземпляр GrpcManager — не тот, что используется всем приложением
grpcManager = GrpcManager()

// SplashActivity.kt:143 — shutdown в finally блока tryRefreshToken
} finally {
    grpcManager.shutdown() // ← канал закрыт
}

// SplashActivity.kt:150 — но loadUserDataAndNavigateToChats использует тот же grpcManager
private suspend fun loadUserDataAndNavigateToChats() {
    // ... создаёт новые клиенты, делает запросы
    grpcManager.createIdentityClient(identityAddress, this) // ← используется после shutdown
}
```

**Вариант решения** — использовать `app.grpcManager` в SplashActivity вместо создания нового:

```kotlin
// ✅ Используем единый app-level GrpcManager
override fun onCreate(savedInstanceState: Bundle?) {
    super.onCreate(savedInstanceState)
    globalParam = GlobalParam(this)
    // Берём тот же GrpcManager что у всего приложения
    val app = application as BarkFluffApplication
    grpcManager = app.grpcManager
    checkDataAndNavigate()
}

// Убрать shutdown() из tryRefreshToken() и loadUserDataAndNavigateToChats(),
// т.к. app-level GrpcManager не должен закрываться между активностями
```

---

### BUG-02 — `LoginActivity` также создаёт свой `GrpcManager`, вызывает его методы, но не вызывает `shutdown`

**Проблема / Описание**
`LoginActivity` создаёт `grpcManager = GrpcManager()` (строка 57) — ещё один отдельный экземпляр. В отличие от `SplashActivity`, `shutdown()` в `LoginActivity` не вызывается нигде. Каналы gRPC остаются открытыми после перехода в `MainActivity`. Это утечка TCP-соединений (Identity канал).

**Путь к файлу:** `LoginActivity.kt` : 57

```kotlin
// LoginActivity.kt:57
// ❌ Утечка: grpcManager не освобождается при выходе из LoginActivity
grpcManager = GrpcManager()
// ... onDestroy не переопределён, shutdown() нигде не вызывается
```

**Вариант решения** — аналогично BUG-01: использовать `app.grpcManager`. Если нужен изолированный GrpcManager — добавить `onDestroy`:

```kotlin
// Вариант А — использовать app.grpcManager (рекомендуется)
grpcManager = (application as BarkFluffApplication).grpcManager

// Вариант Б — если нужен изолированный экземпляр, добавить очистку:
override fun onDestroy() {
    super.onDestroy()
    // ✅ Освобождаем каналы при уничтожении активности
    if (grpcManager !== (application as BarkFluffApplication).grpcManager) {
        grpcManager.shutdown()
    }
}
```

---

### BUG-03 — Нет проверки размера файла перед загрузкой на сервер

**Проблема / Описание**
Функции `readBytesFromUri` и `uploadFile` в `ChatActivity` / `ChatRepository` не проверяют размер файла перед отправкой. Пользователь может прикрепить файл размером несколько ГБ. Это приведёт к: (1) OutOfMemoryError при `readBytes()`, (2) очень долгой загрузке, (3) ошибке 413 от сервера без понятного сообщения пользователю.

**Путь к файлу:** `ChatActivity.kt` : 1050–1058; `repository/ChatRepository.kt` : 207–300

```kotlin
// ChatActivity.kt:1050
// ❌ Нет проверки размера — OOM при большом файле
private suspend fun readBytesFromUri(uri: Uri): ByteArray? = withContext(Dispatchers.IO) {
    try {
        contentResolver.openInputStream(uri)?.use { inputStream ->
            inputStream.readBytes() // ← может попытаться загрузить 5 ГБ в RAM
        }
    } catch (e: Exception) { ... }
}
```

**Вариант решения** — проверять размер через `ContentResolver` перед чтением:

```kotlin
// ✅ Проверяем размер файла перед загрузкой в память
private const val MAX_FILE_SIZE_BYTES = 100 * 1024 * 1024L // 100 МБ

private suspend fun readBytesFromUri(uri: Uri): ByteArray? = withContext(Dispatchers.IO) {
    try {
        // Проверяем размер через OpenableColumns
        val fileSize = contentResolver.query(uri, arrayOf(OpenableColumns.SIZE), null, null, null)
            ?.use { cursor ->
                if (cursor.moveToFirst()) cursor.getLong(0) else -1L
            } ?: -1L

        if (fileSize > MAX_FILE_SIZE_BYTES) {
            withContext(Dispatchers.Main) {
                Toast.makeText(this@ChatActivity, "Файл слишком большой (макс. 100 МБ)", Toast.LENGTH_SHORT).show()
            }
            return@withContext null
        }

        contentResolver.openInputStream(uri)?.use { it.readBytes() }
    } catch (e: Exception) {
        Log.e(TAG, "Error reading bytes from uri", e)
        null
    }
}
```

---

### BUG-04 — `NotificationHelper.recentlyShownMessages` — `mutableSetOf()` без синхронизации при очистке

**Проблема / Описание**
`recentlyShownMessages` объявлен как `mutableSetOf<Long>()` (HashSet), доступ к нему синхронизирован через `synchronized` в `showMessageNotification`, но при очистке (`recentlyShownMessages.clear()`) вместо вытеснения одного элемента очищается весь set. Это значит, что при достижении лимита в 100 сообщений **все** ID сбрасываются, и ближайшие ~100 уведомлений могут задублироваться (Firebase push + реальтайм стрим могут показать одно и то же сообщение дважды).

**Путь к файлу:** `notifications/NotificationHelper.kt` : 119–129

```kotlin
// NotificationHelper.kt:119
synchronized(recentlyShownMessages) {
    if (messageId > 0 && recentlyShownMessages.contains(messageId)) return
    if (messageId > 0) {
        recentlyShownMessages.add(messageId)
        if (recentlyShownMessages.size > DEDUP_MAX_SIZE) {
            recentlyShownMessages.clear() // ❌ Сбрасываем ВСЕ ID — дедупликация ломается
        }
    }
}
```

**Вариант решения** — использовать LRU-подобную очередь, удалять только старейший элемент (аналогично тому, как сделано в `RealtimeService.seenMessageIds`):

```kotlin
// ✅ Удаляем только самый старый элемент, а не весь set
synchronized(recentlyShownMessages) {
    if (messageId > 0 && recentlyShownMessages.contains(messageId)) return
    if (messageId > 0) {
        if (recentlyShownMessages.size >= DEDUP_MAX_SIZE) {
            // Удаляем первый (самый старый) элемент
            val iter = recentlyShownMessages.iterator()
            if (iter.hasNext()) { iter.next(); iter.remove() }
        }
        recentlyShownMessages.add(messageId)
    }
}
```

> **Примечание:** `recentlyShownMessages` нужно объявить как `LinkedHashSet<Long>()` чтобы гарантировать порядок итерации.

---

### BUG-05 — `createChannel` не обрабатывает URL без порта — `parts[1].toInt()` бросит `IndexOutOfBoundsException`

**Проблема / Описание**
В `createChannel` адрес парсится жёсткой разбивкой по `:`. Если пользователь введёт URL без порта (например `https://beacon.barkfluff.com` без `:443`), то `parts[1]` вызовет `IndexOutOfBoundsException`, а вся инициализация gRPC упадёт с непонятной ошибкой.

**Путь к файлу:** `grpc/GrpcManager.kt` : 730–732

```kotlin
// GrpcManager.kt:730
// ❌ Если порт не указан в URL — IndexOutOfBoundsException
val hostPort = url.removePrefix("http://").removePrefix("https://")
val parts = hostPort.split(":")
val host = parts[0]
val port = parts[1].toInt() // ← crash если port не указан
```

**Вариант решения** — задавать дефолтный порт:

```kotlin
// ✅ Обработка URL без порта
val hostPort = url.removePrefix("http://").removePrefix("https://")
val parts = hostPort.split(":")
val host = parts[0]
// Дефолтные порты: HTTPS → 443, HTTP → 80
val defaultPort = if (useTls) 443 else 80
val port = parts.getOrNull(1)?.toIntOrNull() ?: defaultPort
```

---

### BUG-06 — `FileUrlCache` хранит URL стикеров и аватаров бессрочно — нет TTL и инвалидации

**Проблема / Описание**
`FileUrlCache` сохраняет `fileId → URL` в SharedPreferences бессрочно. Presigned URL файлов (S3/MinIO) имеют ограниченный срок действия (обычно 1–24 часа). После истечения URL Coil/OkHttp получит `403 Forbidden`, изображение не загрузится, но кешированный URL останется в SharedPreferences навсегда. При следующем запуске приложения будет использоваться устаревший URL снова.

**Путь к файлу:** `utils/FileUrlCache.kt` : 59–66

```kotlin
// FileUrlCache.kt:59
// ❌ URL сохраняется навсегда, нет TTL
suspend fun putUrl(fileId: String, url: String) = withContext(Dispatchers.IO) {
    synchronized(this@FileUrlCache) {
        val hashedKey = hashKey(fileId)
        cache[hashedKey] = url                                   // в памяти навсегда
        sharedPreferences.edit().putString(hashedKey, url).apply() // на диске навсегда
    }
}
```

**Вариант решения** — хранить пару `(url, expiresAt)`, инвалидировать при чтении:

```kotlin
// ✅ Хранение с TTL (пример: 6 часов)
private const val URL_TTL_MS = 6 * 60 * 60 * 1000L // 6 часов

// Сохранение: ключ "hash_expires" → timestamp
suspend fun putUrl(fileId: String, url: String) = withContext(Dispatchers.IO) {
    synchronized(this@FileUrlCache) {
        val key = hashKey(fileId)
        val expiresAt = System.currentTimeMillis() + URL_TTL_MS
        cache[key] = url
        sharedPreferences.edit()
            .putString(key, url)
            .putLong("${key}_exp", expiresAt)
            .apply()
    }
}

// Чтение: проверяем не истёк ли TTL
fun getUrl(fileId: String): String? {
    synchronized(this@FileUrlCache) {
        val key = hashKey(fileId)
        val expiresAt = sharedPreferences.getLong("${key}_exp", 0L)
        if (System.currentTimeMillis() > expiresAt) {
            // URL просрочен — удаляем
            cache.remove(key)
            return null
        }
        return cache[key]
    }
}
```

---

### BUG-07 — `LogoutHelper`: серверный logout выполняется ПОСЛЕ очистки `GlobalParam`, использует токен из канала

**Проблема / Описание**
Порядок операций в `LogoutHelper.performFullLogout`: сначала очищается `GlobalParam` (шаг 3, строка 71), потом вызывается `grpcManager.logout()` (шаг 4, строка 77). `AuthInterceptor` читает токен из `GlobalParam.accessToken` на каждый вызов. После `clearUserData()` `accessToken` == null. Значит logout-запрос уйдёт на сервер **без токена авторизации** — сервер вернёт 401, а refresh-токен на сервере не удалится. Это не влияет на локальный UX (пользователь всё равно будет разлогинен), но refresh-токен останется активным на сервере, что является проблемой безопасности.

**Путь к файлу:** `utils/LogoutHelper.kt` : 68–85

```kotlin
// LogoutHelper.kt:70
val globalParam = GlobalParam(context)
globalParam.clearUserData() // ← шаг 3: токены очищены из GlobalParam

// LogoutHelper.kt:77
val result = grpcManager.logout() // ← шаг 4: AuthInterceptor не найдёт токен
// → запрос уйдёт без x-auth-token → сервер вернёт 401
// → refresh-токен на сервере НЕ удалён
```

**Вариант решения** — выполнять серверный logout ДО очистки GlobalParam:

```kotlin
// ✅ Сначала серверный logout (пока токен ещё в GlobalParam), потом очистка
suspend fun performFullLogout(context: Context, grpcManager: GrpcManager) {
    // 1. Серверный разлогин — ПЕРВЫМ, пока токен доступен
    try {
        val result = grpcManager.logout()
        if (result.isSuccess) {
            Log.i(TAG, "Серверный разлогин выполнен успешно")
        } else {
            Log.w(TAG, "Серверный разлогин завершился с ошибкой: ${result.exceptionOrNull()?.message}")
        }
    } catch (e: Exception) {
        Log.e(TAG, "Исключение при серверном разлогине", e)
    }

    // 2. Удаление FCM-токена
    try {
        withContext(Dispatchers.IO) {
            FirebaseMessaging.getInstance().deleteToken().await()
        }
    } catch (e: Exception) {
        Log.e(TAG, "Ошибка удаления Firebase токена", e)
    }

    // 3. Очистка кешей
    try { AvatarLoader.clearAllCaches(context) } catch (e: Exception) { ... }
    try { StickerCache.clear() } catch (e: Exception) { ... }
    try {
        val mediaCacheDir = File(context.cacheDir, "media_files")
        if (mediaCacheDir.exists()) { mediaCacheDir.deleteRecursively(); mediaCacheDir.mkdirs() }
    } catch (e: Exception) { ... }

    // 4. Очистка GlobalParam — последней
    GlobalParam(context).clearUserData()

    // 5. Переход на LoginActivity
    val intent = Intent(context, LoginActivity::class.java).apply {
        flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
    }
    context.startActivity(intent)
}
```

---

## 🔵 Прочее / Технический долг

---

### DEBT-01 — `MasterKeys.AES256_GCM_SPEC` (deprecated API) в GlobalParam

**Проблема / Описание**
`GlobalParam` использует `MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC)` — устаревший API из `security-crypto:1.0.x`. Начиная с `security-crypto:1.1.0-alpha`, рекомендуется `MasterKey.Builder`. Старый API не поддерживает BiometricPrompt и `StrongBoxSecurityChip`.

**Путь к файлу:** `data/GlobalParam.kt` : 22–30

```kotlin
// GlobalParam.kt:22
// ⚠️ Deprecated API — не поддерживает StrongBox и Biometric prompt
val masterKeyAlias = MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC)
EncryptedSharedPreferences.create(
    "barkfluff_secure_prefs",
    masterKeyAlias,
    context,
    ...
)
```

**Вариант решения:**

```kotlin
// ✅ Современный API (security-crypto:1.1.0-alpha06+)
import androidx.security.crypto.MasterKey

private val securePreferences: SharedPreferences by lazy {
    val masterKey = MasterKey.Builder(context)
        .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
        .build()
    EncryptedSharedPreferences.create(
        context,
        "barkfluff_secure_prefs",
        masterKey,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )
}
```

---

### DEBT-02 — Дублирование кода создания gRPC-каналов в `GrpcManager`

**Проблема / Описание**
Методы `createIdentityClient`, `createUsersClient`, `createFilesClient`, `createMessagesClient` и другие содержат идентичные блоки: нормализация URL → создание канала → добавление interceptors → создание stub. Это ~40 строк copy-paste на каждый клиент (9 клиентов = ~360 строк дублирования). Изменение логики (например, добавление retry-политики) требует правки во всех 9 местах.

**Путь к файлу:** `grpc/GrpcManager.kt` : 194–870 (блоки создания клиентов)

```kotlin
// ❌ Один из 9 идентичных блоков — разница только в типах клиента и сервиса
fun createFilesClient(filesAddress: String, context: Context? = null, ...): Result<Unit> {
    val normalized = ensureHttpPrefix(filesAddress)
    if (this.filesAddress == normalized && filesClient != null) return Result.success(Unit)
    return try {
        val channel = createChannel(normalized)
        val interceptors = mutableListOf<ClientInterceptor>()
        if (context != null) interceptors.add(AuthInterceptor(context))
        if (includeDeviceInfo && context != null) interceptors.add(DeviceInfoInterceptor(context))
        val interceptedChannel = if (interceptors.isNotEmpty())
            ClientInterceptors.intercept(channel, *interceptors.toTypedArray()) else channel
        filesChannel = interceptedChannel
        filesClient = FilesApiGrpcKt.FilesApiCoroutineStub(interceptedChannel)
        this.filesAddress = normalized
        Result.success(Unit)
    } catch (e: Exception) { Result.failure(...) }
}
// ... повторяется 8 раз
```

**Вариант решения** — вынести общую логику в inline-функцию:

```kotlin
// ✅ Обобщённый создатель клиентов
private inline fun <reified T> createClientInternal(
    address: String,
    currentAddress: String?,
    currentClient: T?,
    context: Context?,
    includeDeviceInfo: Boolean,
    stubFactory: (Channel) -> T,
    onSuccess: (channel: Channel, stub: T, normalized: String) -> Unit,
    errorMessage: String
): Result<Unit> {
    if (address.isBlank()) return Result.failure(IllegalArgumentException("Адрес не указан"))
    val normalized = ensureHttpPrefix(address)
    if (currentAddress == normalized && currentClient != null) return Result.success(Unit)

    return try {
        val channel = createChannel(normalized)
        val interceptors = buildList {
            if (context != null) add(AuthInterceptor(context))
            if (includeDeviceInfo && context != null) add(DeviceInfoInterceptor(context))
        }
        val interceptedChannel = if (interceptors.isNotEmpty())
            ClientInterceptors.intercept(channel, *interceptors.toTypedArray()) else channel
        val stub = stubFactory(interceptedChannel)
        onSuccess(interceptedChannel, stub, normalized)
        Result.success(Unit)
    } catch (e: Exception) {
        Log.e(TAG, errorMessage, e)
        Result.failure(Exception("$errorMessage: ${e.message}"))
    }
}
```

---

### DEBT-03 — `NotificationHelper.recentlyShownMessages` — публичное изменяемое поле

**Проблема / Описание**
`recentlyShownMessages` объявлен как `val recentlyShownMessages = mutableSetOf<Long>()` с модификатором доступа по умолчанию (public). Это позволяет любому коду извне изменять set напрямую, обходя синхронизацию через `synchronized`. `BarkFluffFirebaseMessagingService` обращается к нему напрямую для дедупликации.

**Путь к файлу:** `notifications/NotificationHelper.kt` : 30

```kotlin
// NotificationHelper.kt:30
// ⚠️ Публичное mutable поле — нарушение инкапсуляции, риск race condition
val recentlyShownMessages = mutableSetOf<Long>()
```

**Вариант решения:**

```kotlin
// ✅ Инкапсулировать — добавить метод checkAndMarkShown()
private val recentlyShownMessages = LinkedHashSet<Long>()

/** Проверяет, было ли уведомление уже показано, и если нет — регистрирует его. Thread-safe. */
fun checkAndMarkShown(messageId: Long): Boolean {
    if (messageId <= 0) return false
    synchronized(recentlyShownMessages) {
        if (recentlyShownMessages.contains(messageId)) return true
        if (recentlyShownMessages.size >= DEDUP_MAX_SIZE) {
            val iter = recentlyShownMessages.iterator()
            if (iter.hasNext()) { iter.next(); iter.remove() }
        }
        recentlyShownMessages.add(messageId)
        return false
    }
}
```

---

*Документ сгенерирован в ходе статического аудита кода. Все проблемы требуют верификации в context runtime поведения приложения.*
