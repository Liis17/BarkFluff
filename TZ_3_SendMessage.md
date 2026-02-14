# Техническое задание: Отправка сообщений

## Этап 3: Отправка сообщений и вложений

---

## 0. Референс: WPF реализация

**ВАЖНО: Смотреть WPF клиент как референс!**

### 0.1 Ключевые файлы WPF клиента

- `ClientComponents/BarkFluff.WebApi.Core/Managers/WebApiFileManager.cs` - загрузка файлов
- `ClientComponents/BarkFluff.WebApi.Core/Managers/WebApiMessageManager.cs` - отправка сообщений
- `UserControls/MessageBubble.xaml.cs` - optimistic UI

### 0.2 Полный флоу отправки сообщения с вложениями (из WPF)

```csharp
// WebApiMessageManager.SendMessage()
var response = await MessagesAC.SendMessageAsync(new SendMessageRequest {
    ChatId = chatId,  // или UserId для личных сообщений
    Message = new OutgoingMessage {
        Text = text,
        FilesIds = { fileIds }  // ID файлов УЖЕ загруженных через FileManager
    }
});

// Возвращается полная модель сообщения
return new MessageModel {
    MessageId = response.Message.Id,
    ChatId = chatId,
    Text = response.Message.Content.Text,
    Attachments = response.Message.Content.Attachments.Select(a => new AttachmentsModel {
        Id = a.Id,
        Type = a.Type,
        PreviewUrl = a.PreviewUrl,
        FileId = a.FileId,
        PreviewFileId = a.PreviewFileId,
        FileName = a.FileName,
        Size = a.AttachmentSize
    }).ToList(),
    SenderId = response.Message.SenderId,
    SentAt = response.Message.SentAt,
    ReadBy = response.Message.ReadBy.ToList()
};
```

### 0.3 Optimistic UI (из WPF MessageBubble.xaml.cs)

```csharp
// При создании pending сообщения:
IsPending = filesId != null && filesId.Count > 0;
_pendingFileIds = filesId ?? new List<string>();

// После успешной отправки:
public void MarkAsSent() {
    if (IsPending) {
        IsPending = false;
        Dispatcher.Invoke(() => UpdateReadStatus());
    }
}

// Обновление ReadBy при получении событий:
public void UpdateReadByList(List<long> newReadBy) {
    ReadBy = newReadBy;
    Dispatcher.Invoke(() => UpdateReadStatus());
}
```

### 0.4 Важные моменты из WPF

1. **Флоу загрузки файла** (см. TZ_0):
   - Проверка storage limit
   - Вычисление SHA256 хеша
   - Проверка CheckFileHash (дедупликация)
   - Получение upload URL
   - Загрузка через multipart/form-data
   - Возврат fileID из GetUploadUrlResponse

2. **Отправка сообщения**:
   - fileIDs - это массив ID уже загруженных файлов
   - Можно отправить по chatId или по userId (для создания нового чата)

3. **Optimistic UI**:
   - IsPending флаг пока файлы загружаются
   - MarkAsSent() вызывается после успешной отправки
   - Локальное сообщение заменяется на серверное

---

## 1. Обзор

### 1.1 Цель
Реализовать полный функционал отправки сообщений:
- Текстовые сообщения с многострочным вводом
- Прикрепление файлов (изображения, видео, документы)
- Отправка по Enter / Cmd+Enter
- Отображение статуса отправки
- Optimistic UI (мгновенное отображение отправленного сообщения)
- Обработка ошибок и повторная отправка

### 1.2 Референс дизайна
- iMessage: минималистичное поле ввода снизу
- Telegram: прикрепление файлов через кнопку
- WhatsApp: галерея для выбора изображений

---

## 2. КРИТИЧЕСКИЕ ЗАМЕЧАНИЯ ПО API

### 2.1 Загрузка файлов - ВНИМАНИЕ!

**FilesRepository НЕ РЕАЛИЗОВАН!** См. ТЗ 2.

**Требуется:** Реализовать FilesRepository ПЕРЕД этим этапом!

### 2.2 MessageService.sendMessage

```swift
func sendMessage(
    chatID: String?,     // nil если отправляем по userID
    userID: Int64?,      // nil если отправляем по chatID
    text: String,        // Текст сообщения
    fileIDs: [String]    // Идентификаторы УЖЕ загруженных файлов
) async throws -> Message
```

**Важно:** fileIDs - это ID файлов, УЖЕ загруженных через FileService!

### 2.3 Процесс отправки вложений

```
1. Пользователь выбирает файл
2. Получаем upload URL через fileService.getUploadURL(fileType: .messageAttachment)
3. Загружаем файл через fileService.uploadFile(data:to:)
4. Получаем fileID из ответа
5. Вызываем messageService.sendMessage с fileIDs
```

### 2.4 FileUploadInfo

```swift
public struct FileUploadInfo: Sendable {
    public let fileID: String      // ID для использования в sendMessage
    public let uploadURL: String   // Куда загружать файл
    public let expiresIn: Int64    // Время жизни URL
}
```

### 2.5 FileService.uploadFile

```swift
func uploadFile(data: Data, to url: URL) async throws {
    var request = URLRequest(url: url)
    request.httpMethod = "POST"
    request.setValue("application/octet-stream", forHTTPHeaderField: "Content-Type")
    request.httpBody = data

    let (_, response) = try await URLSession.shared.data(for: request)

    guard let httpResponse = response as? HTTPURLResponse,
          (200...299).contains(httpResponse.statusCode) else {
        throw BFError.networkError("Upload failed")
    }
}
```

### 2.6 Optimistic UI

При отправке сообщения:
1. Создать локальный Message с локальным ID (отрицательный)
2. Добавить в массив messages с флагом `isPending = true`
3. Отправить на сервер
4. При успехе - заменить локальный на настоящий
5. При ошибке - установить `sendError`

---

## 2.7 Полный флоу загрузки файла (Важно! См. TZ_0)

**ВНИМАНИЕ:** Флоу загрузки файла подробно описан в TZ_0 (FilesRepository).

Кратко:

```
1. Проверка storage limit (опционально)
   ↓
2. Вычисление SHA256 хеша файла
   ↓
3. Проверка CheckFileHash
   ├─ Если файл найден → вернуть fileID (БЕЗ загрузки!)
   └─ Если не найден → продолжить
   ↓
4. Получение upload URL через GetUploadUrl
   ├─ uploadURL - куда загружать
   └─ fileID - ID для использования в sendMessage
   ↓
5. Загрузка через HTTP POST multipart/form-data
   ├─ Правильный Content-Type по расширению
   └─ Санитизация имени файла
   ↓
6. Вернуть fileID из GetUploadUrlResponse
```

**Важно:** fileID возвращается из GetUploadUrlResponse, НЕ из ответа upload!

---

## 2. Архитектура

### 2.1 Структура файлов

```
Mac/Barkfluff/Barkfluff/Features/Conversation/
├── Views/
│   ├── MessageInputView.swift             # Переработать
│   ├── Input/
│   │   ├── MessageInputContainerView.swift  # NEW: Контейнер поля ввода
│   │   ├── MessageTextView.swift            # NEW: Многострочный TextView
│   │   ├── AttachmentPreviewBar.swift       # NEW: Превью выбранных файлов
│   │   ├── InputToolbarView.swift           # NEW: Панель с кнопками
│   │   └── ReplyPreviewView.swift           # NEW: Превью ответа на сообщение
│   ├── Pickers/
│   │   ├── MediaPickerView.swift            # NEW: Выбор медиа из галереи
│   │   └── DocumentPickerView.swift         # NEW: Выбор документов
│   └── Components/
│       ├── AttachmentPreviewChip.swift      # Перенести из AttachmentPickerView
│       ├── SendButton.swift                 # NEW: Кнопка отправки с анимацией
│       └── TypingIndicator.swift            # NEW: Индикатор набора текста
├── ViewModels/
│   ├── MessageInputViewModel.swift          # NEW: ViewModel для ввода
│   └── AttachmentUploadViewModel.swift      # NEW: ViewModel для загрузки файлов
└── Helpers/
    ├── PasteboardHelper.swift               # NEW: Обработка вставки из буфера
    └── MessageSender.swift                  # NEW: Логика отправки
```

### 2.2 Зависимости

- `MessageServiceProtocol` - отправка сообщений
- `FileServiceProtocol` - загрузка файлов, получение URL
- `ChatServiceProtocol` - информация о чате
- `DependencyContainer` - внедрение сервисов

---

## 3. Компоненты (подробно)

### 3.1 MessageInputView (переработка)

**Ответственность:** Контейнер для всех элементов ввода

```swift
struct MessageInputView: View {
    let chatID: String
    let replyToMessage: Message?
    @Environment(DependencyContainer.self) private var container

    @State private var viewModel: MessageInputViewModel?
    @State private var text = ""
    @State private var selectedAttachments: [SelectedAttachment] = []
    @State private var isMediaPickerPresented = false
    @State private var isDocumentPickerPresented = false

    var body: some View {
        VStack(spacing: 0) {
            // Превью ответа (если есть)
            if let replyToMessage {
                ReplyPreviewView(message: replyToMessage) {
                    // Отмена ответа
                }
                .padding(.horizontal)
                .padding(.top, 8)
            }

            // Превью выбранных вложений
            if !selectedAttachments.isEmpty {
                AttachmentPreviewBar(
                    attachments: $selectedAttachments
                )
                .padding(.horizontal)
                .padding(.top, 4)
            }

            // Основное поле ввода
            HStack(alignment: .bottom, spacing: 8) {
                // Кнопка прикрепления
                attachmentButton

                // Текстовое поле
                MessageTextView(
                    text: $text,
                    placeholder: "Сообщение...",
                    maxHeight: 120,
                    onSubmit: sendMessage
                )

                // Кнопка отправки
                SendButton(
                    isEnabled: canSend,
                    isLoading: isSending
                ) {
                    sendMessage()
                }
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
        }
        .background(.bar)
        .overlay(alignment: .top) {
            Divider()
        }
        .sheet(isPresented: $isMediaPickerPresented) {
            MediaPickerView { urls in
                selectedAttachments.append(contentsOf: urls.map { .url($0) })
            }
        }
        .fileImporter(
            isPresented: $isDocumentPickerPresented,
            allowedContentTypes: [.item],
            allowsMultipleSelection: true
        ) { result in
            if case .success(let urls) = result {
                selectedAttachments.append(contentsOf: urls.map { .url($0) })
            }
        }
        .onPasteCommand(for: [.image, .fileURL]) { providers in
            handlePaste(providers: providers)
        }
    }

    // MARK: - Computed Properties

    private var canSend: Bool {
        !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ||
        !selectedAttachments.isEmpty
    }

    private var isSending: Bool {
        viewModel?.isSending ?? false
    }

    // MARK: - Components

    private var attachmentButton: some View {
        Menu {
            Button {
                isMediaPickerPresented = true
            } label: {
                Label("Фото или видео", systemImage: "photo")
            }

            Button {
                isDocumentPickerPresented = true
            } label: {
                Label("Документ", systemImage: "doc")
            }
        } label: {
            Image(systemName: "plus.circle.fill")
                .font(.title2)
                .foregroundStyle(.secondary)
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
    }

    // MARK: - Actions

    private func sendMessage() {
        guard canSend, !isSending else { return }

        Task {
            await viewModel?.send(
                text: text,
                attachments: selectedAttachments,
                replyTo: replyToMessage?.id
            )

            // Очистка после отправки
            text = ""
            selectedAttachments = []
        }
    }

    private func handlePaste(providers: [NSItemProvider]) {
        for provider in providers {
            if provider.hasItemConformingToTypeIdentifier(UTType.image.identifier) {
                provider.loadItem(forTypeIdentifier: UTType.image.identifier) { item, _ in
                    if let data = item as? Data,
                       let image = NSImage(data: data) {
                        // Добавить изображение во вложения
                    }
                }
            }
        }
    }
}
```

---

### 3.2 MessageTextView (NEW)

**Ответственность:** Многострочное текстовое поле с автовысотой

```swift
struct MessageTextView: View {
    @Binding var text: String
    let placeholder: String
    let maxHeight: CGFloat
    let onSubmit: () -> Void

    @FocusState private var isFocused: Bool
    @State private var height: CGFloat = 24

    var body: some View {
        ZStack(alignment: .topLeading) {
            // Placeholder
            if text.isEmpty {
                Text(placeholder)
                    .font(.body)
                    .foregroundStyle(.tertiary)
                    .padding(.leading, 4)
                    .padding(.top, 8)
            }

            // TextEditor
            TextEditor(text: $text)
                .font(.body)
                .scrollContentBackground(.hidden)
                .focused($isFocused)
                .frame(height: min(height, maxHeight))
                .frame(minHeight: 24)
                .background(
                    GeometryReader { geometry in
                        Color.clear.preference(
                            key: HeightPreferenceKey.self,
                            value: geometry.size.height
                        )
                    }
                )
        }
        .onPreferenceChange(HeightPreferenceKey.self) { newHeight in
            height = newHeight
        }
        .onSubmit {
            // Cmd+Enter отправляет, Enter - новая строка
            if NSEvent.modifierFlags.contains(.command) {
                onSubmit()
            }
        }
    }
}

private struct HeightPreferenceKey: PreferenceKey {
    static var defaultValue: CGFloat = 24
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) {
        value = nextValue()
    }
}
```

**Клавиатурные сочетания:**
- `Enter` - новая строка
- `Cmd+Enter` - отправка сообщения
- `Shift+Enter` - новая строка
- `Escape` - очистить поле / закрыть ответ

---

### 3.3 AttachmentPreviewBar (NEW)

**Ответственность:** Горизонтальный список превью выбранных файлов

```swift
struct AttachmentPreviewBar: View {
    @Binding var attachments: [SelectedAttachment]

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(attachments) { attachment in
                    AttachmentPreviewChip(attachment: attachment) {
                        removeAttachment(attachment)
                    }
                }
            }
            .padding(.vertical, 4)
        }
        .frame(height: 60)
    }

    private func removeAttachment(_ attachment: SelectedAttachment) {
        attachments.removeAll { $0.id == attachment.id }
    }
}

// MARK: - SelectedAttachment

enum SelectedAttachment: Identifiable {
    case url(URL)
    case image(NSImage)
    case data(Data, filename: String)

    var id: String {
        switch self {
        case .url(let url):
            return url.absoluteString
        case .image:
            return UUID().uuidString
        case .data(_, let filename):
            return filename
        }
    }

    var fileName: String {
        switch self {
        case .url(let url):
            return url.lastPathComponent
        case .image:
            return "image.png"
        case .data(_, let filename):
            return filename
        }
    }
}
```

---

### 3.4 AttachmentPreviewChip (переработка)

**Ответственность:** Превью одного выбранного файла с возможностью удаления

```swift
struct AttachmentPreviewChip: View {
    let attachment: SelectedAttachment
    let onRemove: () -> Void

    @State private var thumbnail: NSImage?

    var body: some View {
        HStack(spacing: 6) {
            // Thumbnail
            Group {
                if let thumbnail {
                    Image(nsImage: thumbnail)
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                } else {
                    iconPlaceholder
                }
            }
            .frame(width: 44, height: 44)
            .clipShape(RoundedRectangle(cornerRadius: 6))

            // Filename
            Text(attachment.fileName)
                .font(.caption)
                .lineLimit(1)
                .frame(maxWidth: 100)

            // Remove button
            Button(action: onRemove) {
                Image(systemName: "xmark.circle.fill")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 6)
        .background(.fill.tertiary)
        .clipShape(Capsule())
        .task {
            await loadThumbnail()
        }
    }

    private var iconPlaceholder: some View {
        RoundedRectangle(cornerRadius: 6)
            .fill(.fill.secondary)
            .overlay {
                Image(systemName: "doc")
                    .font(.title3)
                    .foregroundStyle(.secondary)
            }
    }

    private func loadThumbnail() async {
        switch attachment {
        case .url(let url):
            // Загрузка превью через QLThumbnailGenerator
            break
        case .image(let nsImage):
            thumbnail = nsImage
        case .data:
            break
        }
    }
}
```

---

### 3.5 SendButton (NEW)

**Ответственность:** Анимированная кнопка отправки

```swift
struct SendButton: View {
    let isEnabled: Bool
    let isLoading: Bool
    let action: () -> Void

    @State private var isPressed = false

    var body: some View {
        Button(action: action) {
            ZStack {
                if isLoading {
                    ProgressView()
                        .scaleEffect(0.8)
                        .tint(.white)
                } else {
                    Image(systemName: "arrow.up")
                        .font(.body)
                        .fontWeight(.semibold)
                        .foregroundStyle(.white)
                }
            }
            .frame(width: 32, height: 32)
            .background {
                Circle()
                    .fill(isEnabled ? Color.accentColor : Color.secondary.opacity(0.3))
            }
            .scaleEffect(isPressed ? 0.9 : 1.0)
            .animation(.easeInOut(duration: 0.1), value: isPressed)
        }
        .buttonStyle(.plain)
        .disabled(!isEnabled || isLoading)
        .simultaneousGesture(
            DragGesture(minimumDistance: 0)
                .onChanged { _ in isPressed = true }
                .onEnded { _ in isPressed = false }
        )
    }
}
```

---

### 3.6 ReplyPreviewView (NEW)

**Ответственность:** Превью сообщения, на которое отвечаем

```swift
struct ReplyPreviewView: View {
    let message: Message
    let onCancel: () -> Void

    var body: some View {
        HStack(spacing: 8) {
            // Вертикальная полоска
            Rectangle()
                .fill(Color.accentColor)
                .frame(width: 3)

            // Превью сообщения
            VStack(alignment: .leading, spacing: 2) {
                Text(message.senderName ?? "Неизвестный")
                    .font(.caption)
                    .fontWeight(.semibold)
                    .foregroundStyle(.accent)

                Text(messagePreview)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }

            Spacer()

            // Кнопка отмены
            Button(action: onCancel) {
                Image(systemName: "xmark.circle.fill")
                    .font(.title3)
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
        }
        .padding(8)
        .background(.fill.tertiary)
        .clipShape(RoundedRectangle(cornerRadius: 8))
    }

    private var messagePreview: String {
        if message.content.hasText {
            return message.content.text
        }
        if let attachment = message.content.attachments.first {
            return attachment.type.previewText(fileName: attachment.fileName)
        }
        return "Сообщение"
    }
}
```

---

### 3.7 MediaPickerView (NEW)

**Ответственность:** Выбор медиа из галереи (Фото)

```swift
struct MediaPickerView: View {
    let onSelect: ([URL]) -> Void
    @Environment(\.dismiss) private var dismiss

    @State private var photos: [PHAsset] = []
    @State private var selectedAssets: Set<PHAsset> = []
    @State private var isLoading = true

    var body: some View {
        NavigationStack {
            Group {
                if isLoading {
                    ProgressView("Загрузка...")
                } else if photos.isEmpty {
                    ContentUnavailableView(
                        "Нет фотографий",
                        systemImage: "photo",
                        description: Text("Разрешите доступ к фотографиям")
                    )
                } else {
                    photoGrid
                }
            }
            .navigationTitle("Выберите фото")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Отмена") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Добавить") {
                        // Конвертировать PHAsset в URL и вернуть
                        dismiss()
                    }
                    .disabled(selectedAssets.isEmpty)
                }
            }
        }
        .frame(minWidth: 500, minHeight: 400)
        .task {
            await loadPhotos()
        }
    }

    private var photoGrid: some View {
        ScrollView {
            LazyVGrid(
                columns: [
                    GridItem(.adaptive(minimum: 100), spacing: 2)
                ],
                spacing: 2
            ) {
                ForEach(photos, id: \.localIdentifier) { asset in
                    PhotoThumbnailView(asset: asset)
                        .aspectRatio(1, contentMode: .fill)
                        .clipShape(RoundedRectangle(cornerRadius: 4))
                        .overlay {
                            if selectedAssets.contains(asset) {
                                selectionOverlay(count: selectedAssets.count)
                            }
                        }
                        .onTapGesture {
                            toggleSelection(asset)
                        }
                }
            }
            .padding()
        }
    }

    private func selectionOverlay(count: Int) -> some View {
        ZStack {
            Color.accentColor.opacity(0.3)
            Circle()
                .fill(Color.accentColor)
                .frame(width: 24, height: 24)
                .overlay {
                    Text("\(count)")
                        .font(.caption2)
                        .fontWeight(.semibold)
                        .foregroundStyle(.white)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topTrailing)
                .padding(4)
        }
    }

    private func toggleSelection(_ asset: PHAsset) {
        if selectedAssets.contains(asset) {
            selectedAssets.remove(asset)
        } else {
            selectedAssets.insert(asset)
        }
    }

    private func loadPhotos() async {
        // Загрузка через PHPhotoLibrary
        let status = await PHPhotoLibrary.requestAuthorization(for: .readWrite)
        guard status == .authorized || status == .limited else {
            isLoading = false
            return
        }

        let fetchOptions = PHFetchOptions()
        fetchOptions.sortDescriptors = [NSSortDescriptor(key: "creationDate", ascending: false)]
        fetchOptions.fetchLimit = 100

        let result = PHAsset.fetchAssets(with: [.image, .video], options: fetchOptions)
        var assets: [PHAsset] = []
        result.enumerateObjects { asset, _, _ in
            assets.append(asset)
        }

        photos = assets
        isLoading = false
    }
}
```

---

## 4. MessageInputViewModel (NEW)

### 4.1 Структура

```swift
import BFCore
import BFNetworking
import CryptoKit

@Observable
final class MessageInputViewModel {
    // MARK: - Dependencies
    private let messageService: MessageServiceProtocol
    private let fileService: FileServiceProtocol
    private let chatID: String
    private let onMessageSent: (Message) -> Void

    // MARK: - State
    var isSending = false
    var uploadProgress: [String: Double] = [:]  // filename -> progress (0.0 - 1.0)
    var errorMessage: String?

    // MARK: - Init

    init(
        messageService: MessageServiceProtocol,
        fileService: FileServiceProtocol,
        chatID: String,
        onMessageSent: @escaping (Message) -> Void
    ) {
        self.messageService = messageService
        self.fileService = fileService
        self.chatID = chatID
        self.onMessageSent = onMessageSent
    }

    // MARK: - Public Methods

    func send(
        text: String,
        attachments: [SelectedAttachment],
        replyTo: Int64?  // Пока не используется в API
    ) async {
        guard !isSending else { return }

        isSending = true
        errorMessage = nil

        do {
            // 1. Загрузить файлы (если есть) - ПОЛНЫЙ ФЛОУ как в WPF
            var fileIDs: [String] = []

            for (index, attachment) in attachments.enumerated() {
                let fileName = attachment.fileName
                uploadProgress[fileName] = 0.0

                do {
                    let fileID = try await uploadAttachment(attachment) { progress in
                        Task { @MainActor in
                            uploadProgress[fileName] = progress
                        }
                    }
                    fileIDs.append(fileID)
                    uploadProgress[fileName] = 1.0
                } catch {
                    uploadProgress.removeValue(forKey: fileName)
                    throw error
                }
            }

            // 2. Отправить сообщение
            let message = try await messageService.sendMessage(
                chatID: chatID,
                userID: nil,
                text: text.trimmingCharacters(in: .whitespacesAndNewlines),
                fileIDs: fileIDs
            )

            // 3. Callback для обновления UI
            onMessageSent(message)

            // 4. Очистить прогресс
            uploadProgress.removeAll()

        } catch {
            errorMessage = error.localizedDescription
            uploadProgress.removeAll()
        }

        isSending = false
    }

    // MARK: - Private Methods

    /// Загрузка вложения с ПОЛНЫМ флоу как в WPF
    /// - Parameters:
    ///   - attachment: Вложение для загрузки
    ///   - progress: Callback для прогресса (0.0 - 1.0)
    /// - Returns: fileID для использования в sendMessage
    private func uploadAttachment(
        _ attachment: SelectedAttachment,
        progress: @escaping (Double) -> Void
    ) async throws -> String {
        progress(0.05)

        // 1. Получить данные файла
        let data: Data
        let fileName: String
        var fileType: UploadFileType

        switch attachment {
        case .url(let url):
            guard url.startAccessingSecurityScopedResource() else {
                throw SendMessageError.fileAccessDenied
            }
            defer { url.stopAccessingSecurityScopedResource() }
            data = try Data(contentsOf: url)
            fileName = url.lastPathComponent
            fileType = UploadFileType.from(extension: url.pathExtension)

        case .image(let nsImage, let name):
            // Оптимизация изображения перед отправкой (как в WPF)
            guard let optimizedData = optimizeImage(nsImage) else {
                throw SendMessageError.imageConversionFailed
            }
            data = optimizedData
            fileName = name ?? "image.png"
            fileType = .messageAttachmentImage

        case .data(let fileData, let name):
            data = fileData
            fileName = name
            fileType = UploadFileType.from(extension: (name as NSString).pathExtension)
        }

        progress(0.1)

        // 2. Проверить лимит хранилища (опционально, как в WPF)
        do {
            let storageInfo = try await fileService.getStorageInfo()
            if Int64(data.count) > storageInfo.availableBytes {
                throw SendMessageError.storageLimitExceeded(
                    required: Int64(data.count),
                    available: storageInfo.availableBytes
                )
            }
        } catch let error as SendMessageError {
            throw error // Пробрасываем ошибку лимита
        } catch {
            // Продолжаем даже если не удалось проверить лимит (как в WPF)
            print("Storage check failed, proceeding: \(error)")
        }

        progress(0.15)

        // 3. Вычислить SHA256 хеш для дедупликации
        let fileHash = Self.computeSHA256(data: data)

        progress(0.2)

        // 4. Проверить дедупликацию - ВАЖНО! (как в WPF)
        do {
            let checkResult = try await fileService.checkFileHash(hash: fileHash)
            if checkResult.exists, let existingFileID = checkResult.fileID {
                // Файл уже есть на сервере - НЕ загружаем повторно!
                print("File already exists, reusing fileID: \(existingFileID)")
                progress(1.0)
                return existingFileID
            }
        } catch {
            // Продолжаем даже если не удалось проверить хеш (как в WPF)
            print("Hash check failed, proceeding: \(error)")
        }

        progress(0.3)

        // 5. Загрузить файл через FileService (который делает всё остальное)
        let fileID = try await fileService.uploadFile(
            data: data,
            fileName: fileName,
            fileType: fileType
        )

        progress(1.0)
        return fileID
    }

    /// Оптимизация изображения перед загрузкой (как в WPF ImageProcessor)
    private func optimizeImage(_ nsImage: NSImage) -> Data? {
        guard let tiffData = nsImage.tiffRepresentation,
              let bitmap = NSBitmapImageRep(data: tiffData) else {
            return nil
        }

        // Если изображение большое - сжать
        let maxDimension: CGFloat = 2048
        let currentSize = nsImage.size

        if currentSize.width > maxDimension || currentSize.height > maxDimension {
            let scale = min(maxDimension / currentSize.width, maxDimension / currentSize.height)
            let newSize = CGSize(width: currentSize.width * scale, height: currentSize.height * scale)

            // Ресайз через NSGraphicsContext
            // ... implementation
        }

        // Конвертировать в JPEG с качеством 0.8
        return bitmap.representation(using: .jpeg, properties: [.compressionFactor: 0.8])
    }

    /// Вычисление SHA256 хеша (как в WPF)
    private nonisolated static func computeSHA256(data: Data) -> String {
        let hash = SHA256.hash(data: data)
        return hash.compactMap { String(format: "%02x", $0) }.joined()
    }
}

// MARK: - Errors

enum SendMessageError: LocalizedError {
    case fileAccessDenied
    case imageConversionFailed
    case invalidUploadURL
    case emptyMessage
    case storageLimitExceeded(required: Int64, available: Int64)

    var errorDescription: String? {
        switch self {
        case .fileAccessDenied:
            return "Нет доступа к файлу"
        case .imageConversionFailed:
            return "Не удалось преобразовать изображение"
        case .invalidUploadURL:
            return "Неверный URL для загрузки"
        case .emptyMessage:
            return "Сообщение не может быть пустым"
        case .storageLimitExceeded(let required, let available):
            return "Недостаточно места в хранилище. Требуется: \(formatBytes(required)), доступно: \(formatBytes(available))"
        }
    }

    private func formatBytes(_ bytes: Int64) -> String {
        let formatter = ByteCountFormatter()
        formatter.allowedUnits = [.useKB, .useMB, .useGB]
        return formatter.string(fromByteCount: bytes)
    }
}
```

---

## 5. Optimistic UI (как в WPF MessageBubble.xaml.cs)

### 5.1 Концепция (из WPF)

При отправке сообщения:
1. Мгновенно добавить сообщение в UI с флагом `isPending: true`
2. Показать индикатор отправки (часики)
3. При успехе - вызвать `MarkAsSent()`
4. При ошибке - показать индикатор ошибки с возможностью повтора

```csharp
// Из WPF MessageBubble.xaml.cs:
IsPending = filesId != null && filesId.Count > 0;
_pendingFileIds = filesId ?? new List<string>();

// После успешной отправки:
if (response.message != null) {
    MessageId = response.message.MessageId.ToString();
    MarkAsSent();  // Снимает флаг IsPending
}

// Обновление статуса прочтения:
public void UpdateReadByList(List<long> newReadBy) {
    ReadBy = newReadBy;
    Dispatcher.Invoke(() => UpdateReadStatus());
}

private void UpdateReadStatus() {
    if (_owner != MessageOwner.Me) {
        ReadStatus.Visibility = Visibility.Collapsed;
        return;
    }

    if (IsPending) {
        // Показать часики - сообщение отправляется
        PendingIcon.Visibility = Visibility.Visible;
        SingleCheckmark.Visibility = Visibility.Collapsed;
        DoubleCheckmark.Visibility = Visibility.Collapsed;
        return;
    }

    PendingIcon.Visibility = Visibility.Collapsed;

    // Проверить, прочитано ли другими
    var readByOthers = ReadBy.Any(id => id != SenderId);

    if (readByOthers) {
        // Две галочки - сообщение прочитано
        SingleCheckmark.Visibility = Visibility.Visible;
        DoubleCheckmark.Visibility = Visibility.Visible;
    } else if (!string.IsNullOrEmpty(MessageId)) {
        // Одна галочка - отправлено, но не прочитано
        SingleCheckmark.Visibility = Visibility.Visible;
        DoubleCheckmark.Visibility = Visibility.Collapsed;
    } else {
        // Нет галочки - не отправлено
        SingleCheckmark.Visibility = Visibility.Collapsed;
        DoubleCheckmark.Visibility = Visibility.Collapsed;
    }
}
```

### 5.2 Расширение модели Message

```swift
// В BFCore/Models/Message.swift добавить:
extension Message {
    /// Локальный флаг для pending сообщений
    public var isPending: Bool = false

    /// Ошибка отправки (если есть)
    public var sendError: Error?

    /// Создать pending сообщение для optimistic UI
    public static func createPending(
        chatID: String,
        senderID: Int64,
        text: String,
        attachments: [MessageAttachment] = []
    ) -> Message {
        Message(
            id: -Int64.random(in: 1...Int64.max),  // Отрицательный ID для локальных
            chatID: chatID,
            senderID: senderID,
            senderName: nil,
            content: MessageContent(text: text, attachments: attachments),
            sentAt: Date(),
            readBy: [senderID],
            isSystem: false,
            isPending: true
        )
    }
}
```

### 5.3 Обновление ConversationViewModel для Optimistic UI

```swift
extension ConversationViewModel {
    /// Отправить сообщение с optimistic UI
    func sendMessageWithOptimisticUI(
        text: String,
        attachments: [SelectedAttachment],
        replyTo: Int64? = nil
    ) async {
        // 1. Создать pending сообщение
        let pendingMessage = Message.createPending(
            chatID: chatID,
            senderID: currentUserID,
            text: text,
            attachments: []  // Вложения добавятся после загрузки
        )

        // 2. Мгновенно добавить в UI
        await MainActor.run {
            messages.append(pendingMessage)
            groupedMessages = MessageGrouper.group(messages, currentUserID: currentUserID)
        }

        // 3. Загрузить файлы и отправить сообщение
        do {
            // Загрузить файлы
            var fileIDs: [String] = []
            for attachment in attachments {
                let fileID = try await fileService.uploadFile(
                    data: try attachment.toData(),
                    fileName: attachment.fileName,
                    fileType: UploadFileType.from(extension: attachment.fileExtension)
                )
                fileIDs.append(fileID)
            }

            // Отправить сообщение
            let sentMessage = try await messageService.sendMessage(
                chatID: chatID,
                userID: nil,
                text: text,
                fileIDs: fileIDs
            )

            // 4. Заменить pending на настоящее сообщение
            await MainActor.run {
                if let index = messages.firstIndex(where: { $0.id == pendingMessage.id }) {
                    messages[index] = sentMessage
                    groupedMessages = MessageGrouper.group(messages, currentUserID: currentUserID)
                }
            }

        } catch {
            // 5. При ошибке - установить sendError
            await MainActor.run {
                if let index = messages.firstIndex(where: { $0.id == pendingMessage.id }) {
                    messages[index].sendError = error
                    messages[index].isPending = false
                }
            }
        }
    }

    /// Повторить отправку failed сообщения
    func retryMessage(_ message: Message) async {
        guard message.sendError != nil else { return }

        // Сбросить ошибку и поставить pending
        await MainActor.run {
            if let index = messages.firstIndex(where: { $0.id == message.id }) {
                messages[index].sendError = nil
                messages[index].isPending = true
            }
        }

        // Повторить отправку...
    }
}
```

### 5.4 Отображение статуса в MessageBubbleView (как в WPF)

```swift
// В MessageBubbleView добавить:
@ViewBuilder
private var statusIndicator: some View {
    if message.isPending {
        // Часики - сообщение отправляется (как в WPF)
        HStack(spacing: 4) {
            Image(systemName: "clock")
                .font(.caption2)
                .foregroundStyle(.secondary)
            Text("Отправка...")
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
    } else if let error = message.sendError {
        // Кнопка повтора при ошибке
        Button {
            // Повторить отправку
            Task { await viewModel.retryMessage(message) }
        } label: {
            HStack(spacing: 4) {
                Image(systemName: "exclamationmark.circle.fill")
                    .foregroundStyle(.red)
                Text("Ошибка")
                    .font(.caption2)
                    .foregroundStyle(.red)
            }
        }
        .buttonStyle(.plain)
        .help(error.localizedDescription)
    } else if isOwn {
        // Статус прочтения (как в WPF)
        HStack(spacing: 4) {
            Text(message.sentAt, style: .time)
                .font(.caption2)
                .foregroundStyle(.tertiary)

            // Одна галочка - отправлено
            Image(systemName: "checkmark")
                .font(.caption2)
                .foregroundStyle(isRead ? .green : .tertiary)

            // Вторая галочка - прочитано
            if isRead {
                Image(systemName: "checkmark")
                    .font(.caption2)
                    .foregroundStyle(.green)
            }
        }
    }
}

private var isRead: Bool {
    // Проверяем, прочитано ли сообщение кем-то кроме отправителя
    message.readBy.contains { $0 != message.senderID }
}
```
```

---

## 6. Обработка Paste (вставка из буфера)

### 6.1 PasteboardHelper

```swift
enum PasteboardHelper {
    /// Извлечь изображения из буфера обмена
    static func extractImages(from pasteboard: NSPasteboard) -> [NSImage] {
        var images: [NSImage] = []

        // Проверяем прямое изображение
        if let image = NSImage(pasteboard: pasteboard) {
            images.append(image)
        }

        // Проверяем файлы
        if let fileURLs = pasteboard.readObjects(forClasses: [NSURL.self], options: nil) as? [URL] {
            for url in fileURLs {
                if url.isImageFile, let image = NSImage(contentsOf: url) {
                    images.append(image)
                }
            }
        }

        return images
    }

    /// Проверить, есть ли изображение в буфере
    static func hasImage(in pasteboard: NSPasteboard) -> Bool {
        pasteboard.canReadObject(forClasses: [NSImage.self, NSURL.self], options: nil)
    }
}

extension URL {
    var isImageFile: Bool {
        let imageExtensions = ["jpg", "jpeg", "png", "gif", "heic", "webp", "bmp"]
        return imageExtensions.contains(pathExtension.lowercased())
    }
}
```

---

## 7. Горячие клавиши

### 7.1 Реализация

```swift
// В MessageInputView добавить:
.focusable()
.onKeyPress(.return, modifiers: .command) {
    sendMessage()
    return .handled
}
.onKeyPress(.return, modifiers: []) {
    // Новая строка - по умолчанию
    return .ignored
}
.onKeyPress(.escape) {
    if !selectedAttachments.isEmpty {
        selectedAttachments = []
        return .handled
    }
    if !text.isEmpty {
        text = ""
        return .handled
    }
    return .ignored
}
```

### 7.2 Список горячих клавиш

| Сочетание | Действие |
|-----------|----------|
| `Cmd+Enter` | Отправить сообщение |
| `Enter` | Новая строка |
| `Shift+Enter` | Новая строка |
| `Escape` | Очистить поле / отменить вложения |
| `Cmd+V` | Вставить изображение из буфера |

---

## 8. Индикатор набора текста

### 8.1 Отправка статуса

```swift
// В MessageInputViewModel добавить:
func sendTypingStatus() async {
    // Отправить событие "печатает" через UpdatesService
    // Debounce: отправлять не чаще раза в 3 секунды
}
```

### 8.2 Отображение (TypingIndicatorView)

```swift
struct TypingIndicatorView: View {
    let userNames: [String]

    @State private var animationOffset: CGFloat = 0

    var body: some View {
        HStack(spacing: 4) {
            // Анимированные точки
            HStack(spacing: 2) {
                ForEach(0..<3) { index in
                    Circle()
                        .fill(Color.secondary)
                        .frame(width: 4, height: 4)
                        .offset(y: sin(animationOffset + Double(index) * 0.5) * 2)
                }
            }

            Text(typingText)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .onAppear {
            withAnimation(.linear(duration: 1).repeatForever(autoreverses: false)) {
                animationOffset = .pi * 2
            }
        }
    }

    private var typingText: String {
        switch userNames.count {
        case 0:
            return ""
        case 1:
            return "\(userNames[0]) печатает..."
        case 2:
            return "\(userNames[0]) и \(userNames[1]) печатают..."
        default:
            return "Несколько человек печатают..."
        }
    }
}
```

---

## 9. Обработка ошибок

### 9.1 Типы ошибок

```swift
enum SendMessageError: LocalizedError {
    case emptyMessage
    case fileTooLarge(size: Int64, maxSize: Int64)
    case unsupportedFileType(String)
    case uploadFailed(Error)
    case networkError(Error)
    case unknown(Error)

    var errorDescription: String? {
        switch self {
        case .emptyMessage:
            return "Сообщение не может быть пустым"
        case .fileTooLarge(let size, let maxSize):
            return "Файл слишком большой (\(formatSize(size))). Максимум: \(formatSize(maxSize))"
        case .unsupportedFileType(let ext):
            return "Тип файла .\(ext) не поддерживается"
        case .uploadFailed(let error):
            return "Ошибка загрузки: \(error.localizedDescription)"
        case .networkError(let error):
            return "Ошибка сети: \(error.localizedDescription)"
        case .unknown(let error):
            return "Неизвестная ошибка: \(error.localizedDescription)"
        }
    }
}
```

### 9.2 Повторная отправка

```swift
// При ошибке показать кнопку "Повторить"
// Сохранить данные сообщения для повторной отправки
struct PendingMessage {
    let text: String
    let attachments: [SelectedAttachment]
    let replyTo: Int64?
    var retryCount: Int = 0
}
```

---

## 10. Требования к производительности

### 10.1 Загрузка файлов
- Асинхронная загрузка с прогрессом
- Параллельная загрузка нескольких файлов
- Отмена загрузки при закрытии окна

### 10.2 Сжатие изображений
- Автоматическое сжатие изображений > 5MB
- Сохранение EXIF-данных
- Генерация превью на клиенте

---

## 11. Accessibility

- VoiceOver: озвучивание статуса отправки
- VoiceOver: описание выбранных вложений
- Поддержка управления клавиатурой

---

## 12. Тестирование

### 12.1 Unit тесты
- `MessageInputViewModel` логика отправки
- `PasteboardHelper` извлечение изображений
- Обработка ошибок

### 12.2 UI тесты
- Отправка текстового сообщения
- Прикрепление и отправка файла
- Горячие клавиши
- Повторная отправка при ошибке

---

## 13. Критерии приёмки

- [ ] Текстовое поле расширяется при вводе многострочного текста
- [ ] Отправка по Cmd+Enter работает
- [ ] Вставка изображений из буфера работает
- [ ] Превью выбранных файлов отображается корректно
- [ ] Загрузка файлов с прогрессом работает
- [ ] Сообщение мгновенно появляется в списке (optimistic UI)
- [ ] При ошибке показывается индикатор с возможностью повтора
- [ ] Статус "печатает" отправляется и отображается
- [ ] Ответ на сообщение работает (reply)

---

## 14. Связанные файлы

### Существующие (модифицировать)
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/MessageInputView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/AttachmentPickerView.swift`

### Новые (создать)
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Input/MessageTextView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Input/AttachmentPreviewBar.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Input/ReplyPreviewView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Input/SendButton.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Pickers/MediaPickerView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/ViewModels/MessageInputViewModel.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Helpers/PasteboardHelper.swift`

---

## 15. Примечания

- Для доступа к фото требуется `NSPhotoLibraryUsageDescription` в Info.plist
- Максимальный размер файла: 100 MB (настраивается на бэкенде)
- Поддерживаемые форматы изображений: JPG, PNG, HEIC, WebP, GIF
- Поддерживаемые форматы видео: MP4, MOV, AVI
- Использовать существующий `FileService` из BFCore для загрузки
