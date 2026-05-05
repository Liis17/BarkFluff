# Аудит проекта: BarkFluff.Client.WPF

> **Дата аудита:** 2025-05  
> **Ветка:** `dev`  
> **Версия .NET:** .NET 9 / .NET 10  
> **Аудитор:** GitHub Copilot (BarkfluffAgent)

---

## Оглавление

- [🔴 Безопасность](#-безопасность)
- [🟠 Производительность и оптимизация](#-производительность-и-оптимизация)
- [🟡 Баги и недоработки](#-баги-и-недоработки)
- [🔵 Качество кода и сопровождаемость](#-качество-кода-и-сопровождаемость)

---

## 🔴 Безопасность

---

### SEC-01 — Хранение пароля в открытом поле строки

**Описание**  
В классе `Login` пароль пользователя копируется из `PasswordBox` в приватное поле `string _password` и существует в памяти процесса как обычная строка .NET. Строки в .NET неизменяемы и не зачищаются сборщиком мусора гарантированно. Поле очищается через `ClearSensitiveData()`, однако к этому моменту данные уже могут присутствовать в нескольких копиях в управляемой куче (интернирование, временные копии при конкатенации/сравнении).

**Конкретная проблема**  
`string _password` хранит пароль от момента нажатия кнопки до `ClearSensitiveData()` — если в процессе между этими точками произойдёт дамп памяти или отладчик сделает снимок кучи, пароль будет виден в открытом виде.

**Путь к файлу:** `Windows/BarkFluff.Client.WPF/Pages/SetupPages/Login.xaml.cs : 18–19, 438–455`

```csharp
// ❌ ПРОБЛЕМА: пароль копируется в обычную строку
private string _password = string.Empty;

// В SignInButton_Click:
_password = PasswordBox.Password;   // строка живёт в куче .NET

var response = await App.ServerCommunication.Authorizations(
    _email, _username, _password, _otpCode, App.GParam);

// Очистка происходит ПОСЛЕ сетевого вызова — уже поздно
ClearSensitiveData();               // _password = string.Empty (старая строка в куче остаётся)
```

**Варианты решения**

**Вариант A** — использовать `SecureString` + маршалинг только в точке передачи в API:
```csharp
// ✅ РЕШЕНИЕ: передаём SecureString напрямую, не копируем в string
using System.Runtime.InteropServices;

private void SignInButton_Click(object sender, RoutedEventArgs e)
{
    // PasswordBox.SecurePassword возвращает SecureString
    using var securePassword = PasswordBox.SecurePassword;

    // Маршалинг только в точке вызова API, сразу освобождаем
    IntPtr bstr = Marshal.SecureStringToBSTR(securePassword);
    try
    {
        string plainForApi = Marshal.PtrToStringBSTR(bstr);
        // Вызываем API — строка создаётся и используется в одном блоке
        var response = await App.ServerCommunication
            .Authorizations(_email, _username, plainForApi, _otpCode, App.GParam);
        // plainForApi выходит из scope и кандидат на GC
    }
    finally
    {
        Marshal.ZeroFreeBSTR(bstr); // Зачищаем нативную память
    }
}
```

---

### SEC-02 — Жёстко закодированный путь к FFmpeg

**Описание**  
В `FFmpegService` константы `FFMPEG_PATH` и `FFPROBE_PATH` жёстко указывают на `C:\ProgramData\ffmpeg\`. Это открывает вектор атаки подмены бинарника: любой процесс с правами записи в `C:\ProgramData\ffmpeg\` может подменить `ffmpeg.exe` на вредоносный — приложение запустит его с аргументами пользователя.

**Конкретная проблема**  
Нет верификации целостности (хэш/подпись), нет проверки `FileVersionInfo`. Путь захардкодирован без возможности конфигурации.

**Путь к файлу:** `Windows/BarkFluff.Client.WPF/Services/App/Converter/FFmpegService.cs : 12–13`

```csharp
// ❌ ПРОБЛЕМА: фиксированный путь + нет проверки подписи
private const string FFMPEG_PATH  = @"C:\ProgramData\ffmpeg\ffmpeg.exe";
private const string FFPROBE_PATH = @"C:\ProgramData\ffmpeg\ffprobe.exe";
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: путь из AppContext + проверка подписи Authenticode
private static readonly string FFMPEG_PATH = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
private static readonly string FFPROBE_PATH = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffprobe.exe");

private static void VerifyBinary(string path)
{
    if (!File.Exists(path))
        throw new FileNotFoundException($"FFmpeg binary not found: {path}");

    // Проверяем цифровую подпись (Authenticode)
    var cert = System.Security.Cryptography.X509Certificates
                    .X509Certificate.CreateFromSignedFile(path);
    // Дополнительно можно сравнить Subject или Thumbprint
}

// Вызов перед запуском:
VerifyBinary(FFMPEG_PATH);
```

---

### SEC-03 — Отсутствие проверки `responseUserData.Error` при входе

**Описание**  
После успешной авторизации код немедленно обращается к полям `responseUserData.Data` без проверки `IsSuccess`. Если сервер вернул ошибку (503, сетевой сбой), свойство `Data` будет `null`, что вызовет `NullReferenceException` — но более критично то, что приложение **не уведомит пользователя** и может попытаться открыть мессенджер с пустым профилем.

**Путь к файлу:** `Windows/BarkFluff.Client.WPF/Pages/SetupPages/Login.xaml.cs : 471–479`

```csharp
// ❌ ПРОБЛЕМА: нет проверки responseUserData.Error.IsSuccess
var responseUserData = await App.ServerCommunication.GetUserData(App.GParam);

App.GParam.UserId    = responseUserData.Data.Id;        // NullReferenceException если Data == null
App.GParam.UserName  = responseUserData.Data.Username;
// ...
App.OpenMessengerPage(); // открываем без гарантии валидных данных
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: явная проверка IsSuccess перед доступом к Data
var responseUserData = await App.ServerCommunication.GetUserData(App.GParam);

if (!responseUserData.Error.IsSuccess || responseUserData.Data == null)
{
    App.ErideMessage.AddMessage(
        $"Не удалось загрузить данные профиля: {responseUserData.Error.ErrorMessage}",
        new Erida { Type = MType.Error });
    SetLoadingState(false);
    return; // не открываем мессенджер с пустым профилем
}

App.GParam.UserId = responseUserData.Data.Id;
// ... остальные поля
App.OpenMessengerPage();
```

---

### SEC-04 — Жёстко закодированный путь к исходному файлу в `IncrementVersion()`

**Описание**  
Метод `IncrementVersion()` в `App.xaml.cs` содержит **абсолютный путь к исходному файлу разработчика** (`K:\source\BarkFluff\...`). Этот код попадает в release-сборку (защищён только `#if DEBUG`, но не `Debugger.IsAttached` — условие есть, однако само тело вызывает `File.WriteAllLines` по абсолютному пути). Если кто-то запустит DEBUG-сборку не на машине разработчика — получит необработанное исключение. Кроме того, путь раскрывает структуру машины разработчика.

**Путь к файлу:** `Windows/BarkFluff.Client.WPF/App.xaml.cs : 430–445`

```csharp
// ❌ ПРОБЛЕМА: абсолютный путь к исходникам захардкодирован в бинарник
var versionFile = "K:\\source\\BarkFluff\\Windows\\BarkFluff.Client.WPF\\Services\\App\\AppVersion.cs";
var lines = System.IO.File.ReadAllLines(versionFile); // исключение на любой другой машине
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ A: убрать авто-инкремент из runtime, перенести в CI/CD (рекомендуется)
// В .csproj:
// <PropertyGroup Condition="'$(Configuration)'=='Debug'">
//   <AssemblyVersion>$(GitVersion_AssemblySemVer)</AssemblyVersion>
// </PropertyGroup>

// ✅ РЕШЕНИЕ B: если нужен runtime-инкремент — использовать relative путь через reflection
#if DEBUG
private void IncrementVersion()
{
    // Получаем путь через атрибут CallerFilePath — работает только в Debug
    // Либо пропускаем запись файла и меняем только in-memory версию
    var versionParts = AppVersion.Version.Split('.');
    if (int.TryParse(versionParts[^1], out int build))
    {
        versionParts[^1] = (build + 1).ToString();
        AppVersion.Version = string.Join(".", versionParts);
    }
    // Не пишем файл — записывать исходники из runtime опасно
}
#endif
```

---

## 🟠 Производительность и оптимизация

---

### PERF-01 — Линейный поиск по `StackPanel.Children` при каждом событии

**Описание**  
В нескольких местах (`RealtimeMessagesController`, `OnReadReceiptReceived`, `AddDateSeparatorIfNeeded`, `ScrollToMessage`) для поиска `ChatItem` или `MessageBubble` используется `foreach` по `StackPanel.Children`. При большом количестве чатов (100+) или сообщений (500+ в истории) каждый входящий realtime-ивент вызывает O(n) обход коллекции UI-элементов в **UI-потоке**.

**Конкретная проблема**  
`OnNewMessageReceived` и `OnReadReceiptReceived` вызываются при каждом сообщении и прочтении. При 200 чатах в списке каждый ивент делает до 200 итераций по `StackPanel.Children` — в UI-потоке, блокируя рендеринг.

**Путь к файлу:**  
- `Pages/Messenger/Controllers/RealtimeMessagesController.cs : 87–103, 278–296`  
- `Pages/Messenger/Controllers/ChatHistoryController.cs : 364–395`

```csharp
// ❌ ПРОБЛЕМА: O(n) поиск в UI-потоке при каждом ивенте
foreach (var child in _chatList.Children)          // до 200 итераций
{
    if (child is ChatItem chatItem && chatItem.ChatId == chatId)
    {
        existingChatItem = chatItem;
        break;
    }
}

// ❌ ПРОБЛЕМА: O(n) поиск по сообщениям при read receipt
foreach (var child in _messageArea.Children)       // до 500+ итераций
{
    if (child is MessageBubble bubble && bubble.MessageId == messageId.ToString())
    {
        bubble.UpdateReadByList(newReadBy);
        break;
    }
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: поддерживать словари chatId -> ChatItem и messageId -> MessageBubble
// В ChatListController:
private readonly Dictionary<string, ChatItem> _chatItemsById = new();

// При добавлении чата:
_chatItemsById[chatId] = messageItem;
_chatList.Children.Add(messageItem);

// При поиске — O(1) вместо O(n):
if (_chatItemsById.TryGetValue(chatId, out var chatItem))
{
    chatItem.TransferMessage = message;
    chatItem.UpdateMessage();
}

// Аналогично для MessageBubble:
private readonly Dictionary<long, MessageBubble> _bubbleById = new();
```

---

### PERF-02 — Каждое новое сообщение запускает новый `DispatcherTimer`

**Описание**  
В `ChatHistoryController.AddMessage()` при каждом добавлении сообщения создаётся **новый** `DispatcherTimer` для отложенной отметки прочтения. Если за короткое время приходит 10 сообщений — создаётся 10 таймеров, каждый вызовет `MarkVisibleMessagesAsRead()`, который сам делает O(n) проход. Таймеры не останавливаются при закрытии чата.

**Путь к файлу:** `Pages/Messenger/Controllers/ChatHistoryController.cs : 303–317`

```csharp
// ❌ ПРОБЛЕМА: новый DispatcherTimer на каждое сообщение
public void AddMessage(UserControl control)
{
    _messageArea.Children.Add(control);
    // ...

    var delayTimer = new DispatcherTimer();                                         // новый объект каждый раз!
    delayTimer.Interval = TimeSpan.FromMilliseconds(MessageReadController.INITIAL_MARK_DELAY_MS);
    delayTimer.Tick += (s, args) =>
    {
        delayTimer.Stop();
        _read.MarkVisibleMessagesAsRead();
    };
    delayTimer.Start();
    // delayTimer нигде не сохраняется — утечка если чат закрывается до срабатывания
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: один переиспользуемый таймер с debounce
private DispatcherTimer? _readMarkDebounce;

public void AddMessage(UserControl control)
{
    _messageArea.Children.Add(control);
    var animation = (Storyboard)_page.FindResource("MessageAppearAnimation");
    Storyboard.SetTarget(animation, control);
    animation.Begin();
    _messageScrollViewer.ScrollToEnd();

    // Перезапускаем один таймер (debounce)
    if (_readMarkDebounce == null)
    {
        _readMarkDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(MessageReadController.INITIAL_MARK_DELAY_MS)
        };
        _readMarkDebounce.Tick += (s, args) =>
        {
            _readMarkDebounce.Stop();
            _read.MarkVisibleMessagesAsRead();
        };
    }
    else
    {
        _readMarkDebounce.Stop(); // сбрасываем, если уже запущен
    }
    _readMarkDebounce.Start();
}

// В Clear():
public void Clear()
{
    _readMarkDebounce?.Stop();
    // ...
}
```

---

### PERF-03 — `lock(_lock)` вокруг LiteDB запросов блокирует UI

**Описание**  
`MessageCacheManager` и `FileCacheService` используют синхронный `lock (_lock)` вокруг всех операций с LiteDB. При вызове из UI-потока (например, `GetCachedFilePath` → вызывается синхронно) это блокирует UI на время I/O дискового хранилища. LiteDB поддерживает асинхронную работу через `LiteDatabaseAsync`.

**Путь к файлу:**  
- `Services/App/Caching/MessageCacheManager.cs : 47–55, 125–180`  
- `Services/App/Caching/FileCacheService.cs : 140–155`

```csharp
// ❌ ПРОБЛЕМА: синхронный lock + дисковый I/O, вызывается из UI-потока
public string GetCachedFilePath(string fileId, FileType fileType, string? providedUrl = null)
{
    // ...
    lock (_lock)                                    // блокирует поток
    {
        var cached = _files.FindOne(x => x.Hash == fileId); // дисковый I/O
        if (cached != null && File.Exists(cached.Path))
            return cached.Path;
    }
    // ...
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: заменить object lock на SemaphoreSlim + использовать async методы LiteDB
private readonly SemaphoreSlim _dbSemaphore = new SemaphoreSlim(1, 1);

public async Task<string> GetCachedFilePathAsync(string fileId, FileType fileType, string? providedUrl = null)
{
    if (string.IsNullOrEmpty(fileId))
        return GetPlaceholder(fileType);

    await _dbSemaphore.WaitAsync();
    try
    {
        var cached = _files.FindOne(x => x.Hash == fileId);
        if (cached != null && File.Exists(cached.Path))
            return cached.Path;
    }
    finally
    {
        _dbSemaphore.Release();
    }

    // Скачиваем без блокировки
    return await DownloadAndCacheFileAsync(fileId, fileType, providedUrl)
           ?? GetPlaceholder(fileType);
}
```

---

### PERF-04 — WebP-конвертация декодирует файл дважды

**Описание**  
В `FileCacheService.CreateImageSource()` WebP-файл считывается с диска (`File.ReadAllBytes`) и полностью декодируется ImageSharp для конвертации в PNG. Затем PNG сохраняется в `MemoryStream` и **снова декодируется** WPF для создания `BitmapImage`. При большом количестве аватаров/стикеров это приводит к двойному расходу CPU и пика RAM.

**Путь к файлу:** `Services/App/Caching/FileCacheService.cs : 253–274`

```csharp
// ❌ ПРОБЛЕМА: двойное декодирование — ImageSharp декодирует WebP, WPF декодирует PNG
var webpBytes = File.ReadAllBytes(path);           // чтение с диска
using var image = Image.Load<Rgba32>(webpBytes);   // декодирование #1
using var ms = new MemoryStream();
image.SaveAsPng(ms);                               // кодирование в PNG
ms.Position = 0;

var pngBitmap = new BitmapImage();
pngBitmap.BeginInit();
pngBitmap.StreamSource = ms;
pngBitmap.CacheOption = BitmapCacheOption.OnLoad;  // декодирование #2 (WPF)
pngBitmap.EndInit();
pngBitmap.Freeze();
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: конвертировать WebP → PNG один раз при кешировании (в DownloadAndCacheFileAsync)
// и сохранять уже как PNG на диск. CreateImageSource будет работать с уже
// готовым PNG — только одно декодирование.

// В DownloadAndCacheFileAsync (уже есть частично):
if (IsWebPContent(bytes))
{
    bytes = ConvertWebPToPng(bytes);
    extension = ".png";              // ✅ сохраняем как PNG
}
await File.WriteAllBytesAsync(filePath, bytes);
// filePath теперь всегда PNG для WebP-источников

// В CreateImageSource — убрать WebP-ветку, она больше не нужна:
private ImageSource CreateImageSource(string path)
{
    try
    {
        // Просто грузим файл — он уже PNG после кеширования
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }
    catch { /* placeholder */ }
}
```

---

### PERF-05 — Отсутствие виртуализации в списке чатов и сообщений

**Описание**  
Список чатов (`StackPanel` в `ChatListController`) и область сообщений (`StackPanel` в `ChatHistoryController`) используют `StackPanel` напрямую. `StackPanel` не поддерживает UI-виртуализацию — все `ChatItem` и `MessageBubble` существуют как живые WPF-объекты одновременно в визуальном дереве, независимо от того, видны они на экране или нет. При 100 чатах и 300+ сообщениях это дополнительная нагрузка на layout-движок и память.

**Путь к файлу:**  
- `Pages/MessengerPage.xaml` (StackPanel для чатов и сообщений)  
- `Pages/Messenger/Controllers/ChatListController.cs`  
- `Pages/Messenger/Controllers/ChatHistoryController.cs`

```xml
<!-- ❌ ПРОБЛЕМА: StackPanel не поддерживает виртуализацию -->
<StackPanel x:Name="ChatList" />
<StackPanel x:Name="MessageArea" />
```

**Варианты решения**

```xml
<!-- ✅ РЕШЕНИЕ: заменить StackPanel на ItemsControl с VirtualizingStackPanel -->
<ItemsControl x:Name="ChatList"
              VirtualizingPanel.IsVirtualizing="True"
              VirtualizingPanel.VirtualizationMode="Recycling"
              ScrollViewer.CanContentScroll="True">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <VirtualizingStackPanel />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
</ItemsControl>
```

> **Примечание:** переход на `ItemsControl` потребует рефакторинга контроллеров для работы с `ObservableCollection<ChatItemViewModel>` вместо прямого управления `Children`. Это более масштабное изменение, но даёт кратный прирост производительности при большом количестве элементов.

---

### PERF-06 — `HashSet<long>` в `RealtimeUpdateService` очищается целиком при переполнении

**Описание**  
Механизм дедупликации сообщений хранит ID в `HashSet<long> _processedMessageIds`. При достижении `MaxProcessedMessagesCacheSize = 1000` весь `HashSet` полностью очищается (`_processedMessageIds.Clear()`). Это означает, что сразу после очистки дедупликация не работает — следующие 1000 сообщений снова могут оказаться дублями.

**Путь к файлу:** `Services/App/RealtimeUpdateService.cs : 275–284`

```csharp
// ❌ ПРОБЛЕМА: полная очистка кеша дедупликации теряет контекст
if (_processedMessageIds.Count > MaxProcessedMessagesCacheSize)
{
    _processedMessageIds.Clear();               // теряем все 1000 ID
    _processedMessageIds.Add(messageId);        // остаётся только текущий
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ A: скользящее окно через Queue (FIFO)
private readonly Queue<long> _processedMessagesQueue = new();
private readonly HashSet<long> _processedMessageIds = new();
private const int MaxProcessedMessagesCacheSize = 1000;

private bool TryMarkAsProcessed(long messageId)
{
    lock (_processedMessagesLock)
    {
        if (!_processedMessageIds.Add(messageId))
            return false; // уже обработано

        _processedMessagesQueue.Enqueue(messageId);

        // Удаляем самые старые записи — не теряем весь кеш
        while (_processedMessagesQueue.Count > MaxProcessedMessagesCacheSize)
        {
            var old = _processedMessagesQueue.Dequeue();
            _processedMessageIds.Remove(old);
        }
        return true;
    }
}
```

---

## 🟡 Баги и недоработки

---

### BUG-01 — `MessageCacheManager.GetMessages()` использует `SentAt` вместо `MessageId` для пагинации

**Описание**  
Метод пагинации сообщений ищет `fromMessage` по `MessageId`, получает его `SentAt`, а затем фильтрует остальные сообщения сравнением `SentAt`. Если два сообщения имеют одинаковый `SentAt` (возможно при быстрой отправке или системных сообщениях), результат пагинации будет некорректным — сообщения могут дублироваться или пропускаться.

**Путь к файлу:** `Services/App/Caching/MessageCacheManager.cs : 90–115`

```csharp
// ❌ БАГ: пагинация через SentAt — не монотонный ключ
var fromMessage = _messages.FindOne(x => x.ChatId == chatId && x.MessageId == fromMessageId);
if (fromMessage == null) return new List<MessageModel>();
var timestamp = fromMessage.SentAt;                             // берём timestamp

return query.Where(x => x.SentAt < timestamp)                  // фильтруем по времени!
            .OrderByDescending(x => x.SentAt)
            .Take(offset)
            .ToList();
// Если два сообщения имеют одинаковое SentAt — одно будет пропущено или задублировано
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: пагинация через MessageId (гарантированно уникальный, монотонный)
public List<MessageModel> GetMessages(string chatId, long fromMessageId, int offset)
{
    lock (_lock)
    {
        if (offset > 0)
        {
            // В прошлое: сообщения с MessageId < fromMessageId
            return _messages
                .Find(x => x.ChatId == chatId && x.MessageId < fromMessageId)
                .OrderByDescending(x => x.MessageId)
                .Take(offset)
                .ToList();
        }
        else if (offset < 0)
        {
            // В будущее: сообщения с MessageId > fromMessageId
            return _messages
                .Find(x => x.ChatId == chatId && x.MessageId > fromMessageId)
                .OrderBy(x => x.MessageId)
                .Take(-offset)
                .ToList();
        }
        return new List<MessageModel>();
    }
}
```

---

### BUG-02 — `MessageSendController.SendTextMessage()` отправляет сообщение без реального ID

**Описание**  
При отправке текстового сообщения `SendTextMessage()` немедленно создаёт `MessageBubble` и добавляет его в UI **до** отправки на сервер. Это правильный паттерн оптимистичного UI — но при этом не передаётся реальный `MessageId` полученный от сервера. В результате пузырь сообщения существует с пустым `MessageId`, и при получении этого же сообщения через realtime-стрим `RealtimeMessagesController` не может сопоставить его с существующим пузырём (проверка `pendingBubble.MessageId == string.Empty` работает только для **первого** ожидающего сообщения от текущего пользователя).

**Путь к файлу:** `Pages/Messenger/Controllers/MessageSendController.cs : 53–90`

```csharp
// ❌ БАГ: сообщение добавляется в UI без асинхронного ожидания ответа сервера
// SendTextMessage() — синхронный метод, не await-ит SendMessage API
var messageControl = new MessageBubble(part, options, new List<string>());
_history.AddMessage(messageControl);                            // добавляем сразу
_tempMessage = string.Empty;
_chatListCtrl.UpdateChatWithMessage(message);
// ← нет вызова App.ServerCommunication.SendMessage() здесь!
// Отправка происходит ВНУТРИ конструктора MessageBubble (второй конструктор)
// через SendMessage() → это async void антипаттерн скрытый в конструкторе
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: вынести отправку в явный async Task, как уже сделано для стикеров
public async Task SendTextMessageAsync()
{
    var text = _textForMessage.Text;
    if (string.IsNullOrEmpty(text)) return;

    _textForMessage.Text = string.Empty;

    var parts = SplitMessage(text, MESSAGE_LIMIT);
    foreach (var part in parts)
    {
        // Создаём pending bubble без ожидания
        var pendingMessage = new MessageModel
        {
            Text = part,
            ChatId = _page.ChatId.Value,
            SenderId = App.GParam.UserId,
            SentAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };
        var messageControl = new MessageBubble(/* pending mode */);
        _history.AddMessage(messageControl);

        // Отправляем и обновляем bubble реальным ID
        var response = await App.ServerCommunication.SendMessage(App.GParam, type, letter);
        if (response.error.IsSuccess && response.message != null)
        {
            messageControl.MessageId = response.message.MessageId.ToString();
            messageControl.MarkAsSent();
        }
        else
        {
            messageControl.MarkFailed(); // показываем ошибку в UI
        }
    }
}
```

---

### BUG-03 — `DeadToken()` создаёт новый `DeviceId` при каждом вызове

**Описание**  
Метод `DeadToken()` в `App.xaml.cs` при инвалидации токена сбрасывает `GParam` и **генерирует новый `DeviceId`** (`GParam.DeviceId = Guid.NewGuid().ToString()`). Это означает, что при каждой сессии с истёкшим токеном устройство регистрируется как новое. Сервер накапливает «мёртвые» устройства, а пользователь видит новое устройство в списке сессий после каждого переподключения.

**Путь к файлу:** `Windows/BarkFluff.Client.WPF/App.xaml.cs : 235–265`

```csharp
// ❌ БАГ: новый DeviceId при каждом dead token — устройство плодит сессии на сервере
GParam = new BarkFluff.WebApi.Core.MessengerData.GlobalParam();
// ... восстанавливаем поля ...
GParam.DeviceId = Guid.NewGuid().ToString();   // ← новый ID каждый раз!
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: сохранять DeviceId при сбросе GParam
public static void DeadToken()
{
    DeadTokenMode = true;

    // Сохраняем поля которые должны пережить сброс
    var savedDeviceId = GParam.DeviceId;    // DeviceId идентифицирует устройство, не сессию
    var ip = GParam.IpAddress;
    // ... остальные поля ...

    GParam = new BarkFluff.WebApi.Core.MessengerData.GlobalParam();
    GParam.DeviceId = savedDeviceId;        // ✅ восстанавливаем тот же ID
    GParam.IpAddress = ip;
    // ...

    MessengerWindow.CloseApp();
}
```

---

### BUG-04 — `AddDateSeparatorIfNeeded` — O(n) поиск по всем детям, может добавить дублирующий разделитель

**Описание**  
Метод итерирует `_messageArea.Children` в поиске существующего `DateHeaderControl` с нужной датой. Но проверяет **все** разделители — если последний разделитель "Сегодня" существует в середине списка (при вставке истории сверху), новый разделитель "Сегодня" всё равно не добавится. Однако если текущий разделитель "Сегодня" находится не последним — логика `lastChild is DateHeaderControl` не сработает и разделитель добавится повторно.

**Путь к файлу:** `Pages/Messenger/Controllers/ChatHistoryController.cs : 358–400`

```csharp
// ❌ БАГ: поиск по всем детям, но решение зависит от ПОСЛЕДНЕГО элемента —
// противоречие: нашли разделитель с нужной датой → выходим (OK),
// но если разделитель есть но не последний — lastChild не DateHeaderControl
// и условие lastMessageLocalDate != newDate может добавить дубль

foreach (var child in _messageArea.Children)   // перебор всех
{
    if (child is DateHeaderControl existingHeader)
    {
        var expectedText = GetDateHeader(newDate);
        if (headerText == expectedText)
            return;           // нашли нужный — выходим
    }
}

// Но дальше:
var lastChild = _messageArea.Children[^1];
if (lastChild is DateHeaderControl) return;    // только если последний

if (lastChild is MessageBubble lastBubble && lastBubble.SentAt != null)
{
    if (lastMessageLocalDate != newDate)
        AddDateHeader(newDate);   // добавит даже если в середине уже есть!
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: хранить текущую дату последнего разделителя отдельным полем
private DateTime? _lastDateSeparatorDate = null;

public void AddDateSeparatorIfNeeded(DateTime newMessageLocalDate)
{
    var newDate = newMessageLocalDate.Date;

    if (_lastDateSeparatorDate.HasValue && _lastDateSeparatorDate.Value == newDate)
        return; // разделитель с этой датой уже добавлен

    var dateHeader = GetDateHeader(newDate);
    var dateControl = new DateHeaderControl { Text = dateHeader };
    dateControl.HorizontalAlignment = HorizontalAlignment.Center;
    dateControl.Margin = new Thickness(0, 10, 0, 10);
    _messageArea.Children.Add(dateControl);

    _lastDateSeparatorDate = newDate;
}

// При сбросе чата:
public void ResetForNewChat(...)
{
    _lastDateSeparatorDate = null;
    // ...
}
```

---

### BUG-05 — `MessageCacheManager` содержит закомментированный `Directory.CreateDirectory`

**Описание**  
В конструкторе `MessageCacheManager` строка создания директории кеша закомментирована. Если `fileCacheDir` не существует, файлы `HttpClient` не смогут сохраниться — `File.WriteAllBytesAsync` выбросит `DirectoryNotFoundException`.

**Путь к файлу:** `Services/App/Caching/MessageCacheManager.cs : 33`

```csharp
// ❌ БАГ: директория может не существовать — исключение при попытке записи
public MessageCacheManager(string dbPath, string fileCacheDir)
{
    _dbPath = dbPath;
    _fileCacheDir = fileCacheDir;
    //Directory.CreateDirectory(fileCacheDir);  // ← ЗАКОММЕНТИРОВАНО!
    _db = new LiteDatabase(_dbPath);
    // ...
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: раскомментировать строку создания директории
public MessageCacheManager(string dbPath, string fileCacheDir)
{
    _dbPath = dbPath;
    _fileCacheDir = fileCacheDir;
    Directory.CreateDirectory(fileCacheDir); // ✅ убедиться что директория существует
    _db = new LiteDatabase(_dbPath);
    // ...
}
```

---

### BUG-06 — `UpdateService` не освобождает `HttpClient` и `Timer`

**Описание**  
`UpdateService` создаёт `HttpClient` и `System.Timers.Timer` в конструкторе, но класс не реализует `IDisposable`. При переинициализации (например, после `DeadToken`) старый экземпляр не освобождается — таймер продолжает стрелять, `HttpClient` держит соединение.

**Путь к файлу:** `Services/App/Update/UpdateService.cs : 25–38`

```csharp
// ❌ БАГ: нет IDisposable, ресурсы не освобождаются
public class UpdateService
{
    private readonly HttpClient _httpClient;           // не disposed
    private readonly System.Timers.Timer _timer;       // продолжит стрелять

    public void Stop()
    {
        _timer.Stop();   // только останавливает, не Dispose()
    }
    // нет Dispose()
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: реализовать IDisposable
public class UpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly System.Timers.Timer _timer;
    private bool _disposed;

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _timer.Stop();
        _timer.Dispose();
        _httpClient.Dispose();
        _disposed = true;
    }
}

// В App.OnExit:
protected override void OnExit(ExitEventArgs e)
{
    updateService?.Stop();
    updateService?.Dispose(); // ✅ добавить
    // ...
}
```

---

### BUG-07 — `MessageBox.Show` с текстом про нереализованные групповые чаты попадёт в продакшн

**Описание**  
В `RealtimeMessagesController.AddNewChatToListAsync()` есть `MessageBox.Show("Групповые чаты не реализованны, пропускаем, для поиска по коду 73248334")`. Это отладочный диалог, который **не убран** и будет показан пользователю при получении сообщения от группового чата в продакшн-сборке.

**Путь к файлу:** `Pages/Messenger/Controllers/RealtimeMessagesController.cs : 213–215`

```csharp
// ❌ БАГ: отладочный MessageBox.Show в логике, выполняемой в продакшн
if (chat.chatInfo.IsGroup)
{
    MessageBox.Show("Групповые чаты не реализованны, пропускаем, для поиска по коду 73248334");
    return; // тихо пропускаем — но пользователь увидел странное окно
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: убрать MessageBox.Show, заменить на лог
if (chat.chatInfo.IsGroup)
{
    App.ErideMessage.AddMessage(
        $"Групповые чаты не поддерживаются (chatId={chatId})",
        new Erida { Type = MType.Debug });
    return;
}
```

---

## 🔵 Качество кода и сопровождаемость

---

### CODE-01 — `App.xaml.cs` — слишком много статических `public` полей

**Описание**  
Класс `App` содержит 12+ статических публичных полей (`ServerCommunication`, `GParam`, `CacheManager`, `FileCacheService`, `Messenger` и др.). Это фактически глобальное состояние приложения без инкапсуляции. Любой класс может читать и писать в эти поля без контроля. При написании тестов или переиспользовании компонентов это создаёт сильную связанность.

**Путь к файлу:** `App.xaml.cs : 34–50`

**Варианты решения**
- Ввести простой `ServiceLocator` или DI-контейнер (например, `Microsoft.Extensions.DependencyInjection`)
- Сделать поля `internal` вместо `public` там где не нужен внешний доступ
- Выделить `AppState` класс с событиями изменения состояния

---

### CODE-02 — `.bak`-файл попал в проект

**Описание**  
В проект включён файл `UserControls/SettingsPages/ChatsSettingsPage.xaml.cs.bak`. Это резервная копия, которая не должна быть частью проекта и компилироваться.

**Путь к файлу:** `Windows/BarkFluff.Client.WPF/UserControls/SettingsPages/ChatsSettingsPage.xaml.cs.bak`

**Варианты решения**
```bash
# Удалить файл и добавить в .gitignore
git rm "Windows/BarkFluff.Client.WPF/UserControls/SettingsPages/ChatsSettingsPage.xaml.cs.bak"
echo "*.bak" >> .gitignore
```

---

### CODE-03 — Закомментированный UI-код в `Login.xaml.cs`

**Описание**  
Методы `ShowError`, `HideError`, `ShowOtpError`, `HideOtpError` содержат большие блоки закомментированного кода (стили для ошибочных полей). Это "мёртвый код", который засоряет файл и вводит в заблуждение при сопровождении.

**Путь к файлу:** `Pages/SetupPages/Login.xaml.cs : 298–345`

```csharp
// ❌ ПРОБЛЕМА: закомментированный код оставлен навсегда
private void ShowError(Control control, TextBlock errorText, string message)
{
    //if (control is TextBox textBox)
    //{
    //    textBox.Style = (Style)FindResource("MinimalTextBoxError");
    //}
    //else if (control is PasswordBox passwordBox)
    //{
    //    passwordBox.Style = (Style)FindResource("MinimalPasswordBoxError");
    //}
    errorText.Text = message;
    errorText.Visibility = Visibility.Visible;
}
```

**Варианты решения**
- Если функциональность нужна — реализовать и раскомментировать
- Если не нужна — удалить, Git хранит историю

---

### CODE-04 — `async void` в `OnStickerSelected` и других обработчиках событий

**Описание**  
Обработчики событий `OnStickerSelected`, `OnPreviewSend` объявлены как `async void`. Исключения внутри `async void` не могут быть перехвачены внешним кодом — они попадают в `UnhandledException` приложения и могут завершить процесс. Для UI-обработчиков событий это стандартная практика, но тело должно быть обёрнуто в `try/catch`.

**Путь к файлу:**  
- `Pages/Messenger/Controllers/MessageSendController.cs : 147`  
- `Pages/Messenger/Controllers/AttachmentController.cs : 130`

```csharp
// ⚠️ ПРОБЛЕМА: async void — необработанное исключение завершает приложение
private async void OnStickerSelected(object? sender, StickerSelectedEventArgs e)
{
    _stickerPopup.IsOpen = false;
    await SendStickerAsync(e);      // если здесь исключение — приложение упадёт
}

private async void OnPreviewSend(object? sender, SendAttachmentsEventArgs e)
{
    // нет try/catch — любое исключение в SendMessageWithAttachments убьёт процесс
    await SendMessageWithAttachments(e.MessageText, e.Attachments);
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: обернуть тело в try/catch
private async void OnStickerSelected(object? sender, StickerSelectedEventArgs e)
{
    try
    {
        _stickerPopup.IsOpen = false;
        await SendStickerAsync(e);
    }
    catch (Exception ex)
    {
        App.ErideMessage.AddMessage(
            $"Ошибка отправки стикера: {ex.Message}",
            new Erida { Type = MType.Error });
    }
}
```

---

### CODE-05 — `Debug.WriteLine` оставлены в `OnPasteCommand` (AttachmentController)

**Описание**  
Метод `OnPasteCommand` содержит 8+ вызовов `System.Diagnostics.Debug.WriteLine`. Это отладочный вывод, который в Release-сборке не компилируется, но засоряет код и в DEBUG-сборке даёт лишний I/O при каждой вставке.

**Путь к файлу:** `Pages/Messenger/Controllers/AttachmentController.cs : 368–430`

```csharp
// ❌ ПРОБЛЕМА: Debug.WriteLine в продуктовом коде
System.Diagnostics.Debug.WriteLine("=== OnPasteCommand вызван ===");
System.Diagnostics.Debug.WriteLine($"FileDrop present: {clipboard.GetDataPresent(DataFormats.FileDrop)}");
System.Diagnostics.Debug.WriteLine($"Bitmap present: {clipboard.GetDataPresent(DataFormats.Bitmap)}");
// ... ещё 5 вызовов
```

**Варианты решения**
- Удалить `Debug.WriteLine` из логики вставки
- Заменить на `App.ErideMessage.AddMessage(..., MType.Debug)` для унификации с остальным логированием

---

*Документ сгенерирован автоматически на основе статического анализа кода. Все проблемы требуют ручной верификации перед исправлением.*
