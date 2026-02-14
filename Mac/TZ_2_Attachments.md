# Техническое задание: Отображение вложений в сообщениях

## Этап 2: Вложения (Изображения, видео, документы)

---

## 0. Референс: WPF реализация

**ВАЖНО: Смотреть WPF клиент как референс!**

### 0.1 Ключевые файлы WPF клиента

- `UserControls/MessageContent/ImageMessageContent.xaml.cs` - отображение изображений
- `UserControls/MessageContent/VideoMessageContent.xaml.cs` - отображение видео
- `UserControls/MessageContent/DocumentMessageContent.xaml.cs` - отображение документов
- `UserControls/MessageContent/MultiImageGrid.xaml.cs` - сетка изображений
- `UserControls/MessageContent/MultiVideoGrid.xaml.cs` - сетка видео
- `UserControls/CachedImage.xaml.cs` - кеширование изображений
- `Services/App/Caching/FileCacheService.cs` - сервис кеширования файлов

### 0.2 Приоритет превью (из ImageMessageContent.xaml.cs)

```csharp
// Используем PreviewFileId для превью в сообщении (как в WPF)
var previewId = !string.IsNullOrEmpty(attachment.PreviewFileId)
    ? attachment.PreviewFileId
    : attachment.FileId;

CachedContentImage.FileId = previewId;
CachedContentImage.FileUrl = attachment.PreviewUrl;
CachedContentImage.FileType = attachment.Type == Gif
    ? FileType.Gif
    : FileType.Image;
```

**Важно:** Приоритет: `PreviewFileId` > `FileId`, а `PreviewUrl` используется как опциональный URL.

### 0.3 FileCacheService (WPF)

```csharp
// Кеширование использует LiteDB для метаданных + disk cache для файлов
// Директории: avatars/, images/, videos/, gifs/, documents/

// Получение файла (асинхронная загрузка если нет в кеше):
public string GetCachedFilePath(string fileId, FileType fileType, string? providedUrl = null) {
    // 1. Проверяем кеш (LiteDB)
    var cached = _files.FindOne(x => x.Hash == fileId);
    if (cached != null && File.Exists(cached.Path))
        return cached.Path;

    // 2. Если нет - запускаем асинхронную загрузку
    _ = Task.Run(() => DownloadAndCacheFileAsync(fileId, fileType, providedUrl));

    // 3. Возвращаем placeholder
    return GetPlaceholder(fileType);
}

// Событие когда файл закеширован
public event Action<string, string, FileType>? FileCached;
```

---

## 1. Обзор

### 1.1 Цель
Реализовать отображение вложений в сообщениях с поддержкой:
- Изображений (с превью и полным просмотром)
- Видео (с превью и плеером)
- GIF-анимаций
- Документов (с иконкой, именем, размером)
- Группировка нескольких вложений в одном сообщении (сетка)

### 1.2 Референс дизайна
- iMessage: отображение медиа в пузырьках
- Telegram: группировка фото в сетку 2x2
- WhatsApp: галерея в сообщении

---

## 2. КРИТИЧЕСКИЕ ЗАМЕЧАНИЯ ПО API

### 2.1 FilesRepository - ВНИМАНИЕ!

**FilesRepository НЕ РЕАЛИЗОВАН!** Все методы выбрасывают `BFNetworkingError.unknown("Not implemented")`.

```swift
// FilesRepository.swift - ТЕКУЩЕЕ СОСТОЯНИЕ:
public func getUploadURL(fileType: FileType) async throws -> FileUploadInfo {
    throw BFNetworkingError.unknown("Not implemented")
}
public func getTempDownloadURL(fileID: String) async throws -> String {
    throw BFNetworkingError.unknown("Not implemented")
}
// ... и так далее
```

**Требуется:** Сначала реализовать FilesRepository перед этим этапом!

### 2.2 FileServiceProtocol (BFCore)

```swift
// Доступные методы (требуют работающего FilesRepository):
func getUploadURL(fileType: FileType) async throws -> FileUploadInfo
func uploadFile(data: Data, to url: URL) async throws
func getDownloadURL(fileID: String) async throws -> String
func checkFileHash(hash: String) async throws -> FileCheckResult
func getStorageInfo() async throws -> StorageInfo
```

### 2.3 FileType enum (BFNetworking)

```swift
public enum FileType: Sendable, Codable {
    case userAvatar
    case chatPicture
    case messageAttachment
    case other
}
```

**Важно:** Для вложений использовать `.messageAttachment`, не специфичные типы!

### 2.4 MessageAttachment (BFCore)

```swift
public struct MessageAttachment: Identifiable, Hashable, Sendable {
    public let id: Int64
    public let type: AttachmentType
    public let fileID: String
    public let fileName: String
    public let fileSize: Int64
    public var previewURL: String?     // Может быть nil!
    public var previewFileID: String?  // Может быть nil!
}

public enum AttachmentType: String, Sendable, Codable {
    case image
    case video
    case gif
    case document
    case audio
}
```

### 2.5 Получение URL для отображения

**Приоритет (как в WPF):**
1. `previewFileID` - если есть, использовать для превью
2. `fileID` - fallback если previewFileID пустой
3. `previewURL` - опциональный URL если есть

```swift
// Приоритет превью (из WPF ImageMessageContent)
var previewId = !attachment.previewFileID.isEmpty
    ? attachment.previewFileID
    : attachment.fileID

// Если нет previewURL, получить через FileService
if attachment.previewURL == nil {
    let downloadURL = try await fileService.getDownloadURL(fileID: previewId)
}
```

---

## 2. Архитектура

### 2.1 Структура файлов

```
Mac/Barkfluff/Barkfluff/Features/Conversation/
├── Views/
│   ├── MessageAttachmentView.swift      # Переработать
│   ├── Attachments/
│   │   ├── AttachmentGridView.swift     # NEW: Сетка вложений
│   │   ├── ImageAttachmentView.swift    # NEW: Изображение
│   │   ├── VideoAttachmentView.swift    # NEW: Видео
│   │   ├── GIFAttachmentView.swift      # NEW: GIF
│   │   ├── DocumentAttachmentView.swift # NEW: Документ
│   │   └── AudioAttachmentView.swift    # NEW: Аудио (опционально)
│   └── Viewers/
│       ├── MediaViewerView.swift        # NEW: Полноэкранный просмотрщик
│       ├── ImageViewerView.swift        # NEW: Просмотр изображений
│       └── VideoPlayerView.swift        # NEW: Видеоплеер
├── ViewModels/
│   └── AttachmentViewModel.swift        # NEW: ViewModel для вложения
└── Helpers/
    ├── AttachmentLayoutCalculator.swift # NEW: Расчёт размеров сетки
    └── FileIconProvider.swift           # NEW: Иконки для типов файлов
```

### 2.2 Зависимости

- `BFCore.MessageAttachment` - модель вложения
- `BFCore.AttachmentType` - тип вложения
- `FileServiceProtocol` - получение URL для скачивания/превью
- `Nuke` (уже подключен) - загрузка и кэширование изображений

---

## 2.5 FileCacheService (NEW) - как в WPF

**Референс:** `BarkFluff.Client.WPF/Services/App/Caching/FileCacheService.cs`

```swift
//
//  FileCacheService.swift
//  Сервис кеширования файлов (аналог WPF)
//

import Foundation
import SwiftUI

/// Типы файлов для кеширования
public enum CachedFileType: String, Sendable {
    case avatar
    case image
    case video
    case gif
    case document
}

/// Сервис кеширования файлов
@Observable
public final class FileCacheService {
    // MARK: - Properties

    private let baseCacheDir: URL
    private let dbPath: URL
    private let fileManager = FileManager.default
    private let httpClient = URLSession.shared
    private let downloadSemaphore = AsyncSemaphore(value: 5) // Max 5 параллельных загрузок

    // Placeholder пути
    public static let imagePlaceholder = "image_placeholder"
    public static let videoPlaceholder = "video_placeholder"
    public static let gifPlaceholder = "gif_placeholder"
    public static let documentPlaceholder = "document_placeholder"

    // MARK: - Events

    /// Событие вызывается когда файл закеширован
    public var onFileCached: ((String, URL, CachedFileType) -> Void)?

    // MARK: - Init

    public init(baseCacheDir: URL, dbPath: URL) {
        self.baseCacheDir = baseCacheDir
        self.dbPath = dbPath

        // Создаем директории для разных типов
        try? fileManager.createDirectory(at: baseCacheDir.appendingPathComponent("images"), withIntermediateDirectories: true)
        try? fileManager.createDirectory(at: baseCacheDir.appendingPathComponent("videos"), withIntermediateDirectories: true)
        try? fileManager.createDirectory(at: baseCacheDir.appendingPathComponent("gifs"), withIntermediateDirectories: true)
        try? fileManager.createDirectory(at: baseCacheDir.appendingPathComponent("documents"), withIntermediateDirectories: true)
        try? fileManager.createDirectory(at: baseCacheDir.appendingPathComponent("avatars"), withIntermediateDirectories: true)
    }

    // MARK: - Public Methods

    /// Получить путь к закешированному файлу или placeholder если нет
    /// Запускает асинхронную загрузку если файл отсутствует
    public func getCachedFilePath(
        fileID: String,
        fileType: CachedFileType,
        providedURL: String? = nil
    ) -> URL {
        guard !fileID.isEmpty else {
            return placeholderURL(for: fileType)
        }

        // Проверяем кеш
        if let cachedPath = checkCache(fileID: fileID) {
            return cachedPath
        }

        // Запускаем асинхронную загрузку
        Task {
            await downloadAndCache(fileID: fileID, fileType: fileType, providedURL: providedURL)
        }

        return placeholderURL(for: fileType)
    }

    /// Асинхронно получить путь к файлу (ждет завершения загрузки)
    public func getCachedFilePathAsync(
        fileID: String,
        fileType: CachedFileType,
        providedURL: String? = nil
    ) async -> URL {
        guard !fileID.isEmpty else {
            return placeholderURL(for: fileType)
        }

        // Проверяем кеш
        if let cachedPath = checkCache(fileID: fileID) {
            return cachedPath
        }

        // Ждем загрузки
        return await downloadAndCache(fileID: fileID, fileType: fileType, providedURL: providedURL)
            ?? placeholderURL(for: fileType)
    }

    /// Проверить, есть ли файл в кеше
    public func isFileCached(fileID: String) -> Bool {
        checkCache(fileID: fileID) != nil
    }

    /// Очистить кеш
    public func clearCache(fileType: CachedFileType? = nil) {
        // Implementation
    }

    // MARK: - Private Methods

    private func checkCache(fileID: String) -> URL? {
        // Проверяем in-memory кеш и disk cache
        let subdirs = ["images", "videos", "gifs", "documents", "avatars"]
        for subdir in subdirs {
            let dir = baseCacheDir.appendingPathComponent(subdir)
            let contents = try? fileManager.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)
            for url in contents ?? [] {
                if url.lastPathComponent.hasPrefix(fileID) {
                    return url
                }
            }
        }
        return nil
    }

    private func downloadAndCache(
        fileID: String,
        fileType: CachedFileType,
        providedURL: String?
    ) async -> URL? {
        await downloadSemaphore.wait()
        defer { downloadSemaphore.signal() }

        // Проверяем еще раз после ожидания
        if let cached = checkCache(fileID: fileID) {
            return cached
        }

        // Получаем URL для скачивания
        var downloadURL: String?
        if let providedURL {
            downloadURL = providedURL
        } else {
            // Получить через FileService
            // downloadURL = try? await fileService.getDownloadURL(fileID: fileID)
        }

        guard let urlString = downloadURL, let url = URL(string: urlString) else {
            return nil
        }

        do {
            let (data, _) = try await httpClient.data(from: url)
            let ext = url.pathExtension.isEmpty ? defaultExtension(for: fileType) : url.pathExtension
            let cacheDir = cacheDirectory(for: fileType)
            let filePath = cacheDir.appendingPathComponent("\(fileID).\(ext)")

            try data.write(to: filePath)

            // Уведомляем о кешировании
            onFileCached?(fileID, filePath, fileType)

            return filePath
        } catch {
            print("Error caching file \(fileID): \(error)")
            return nil
        }
    }

    private func placeholderURL(for fileType: CachedFileType) -> URL {
        // Возвращаем bundle URL или asset name
        switch fileType {
        case .avatar:
            return Bundle.main.url(forResource: "userplaceholder", withExtension: "png")!
        case .image:
            return Bundle.main.url(forResource: "image_placeholder", withExtension: "png")!
        case .video:
            return Bundle.main.url(forResource: "video_placeholder", withExtension: "png")!
        case .gif:
            return Bundle.main.url(forResource: "gif_placeholder", withExtension: "png")!
        case .document:
            return Bundle.main.url(forResource: "document_placeholder", withExtension: "png")!
        }
    }

    private func cacheDirectory(for fileType: CachedFileType) -> URL {
        let subdir: String
        switch fileType {
        case .avatar: subdir = "avatars"
        case .image: subdir = "images"
        case .video: subdir = "videos"
        case .gif: subdir = "gifs"
        case .document: subdir = "documents"
        }
        return baseCacheDir.appendingPathComponent(subdir)
    }

    private func defaultExtension(for fileType: CachedFileType) -> String {
        switch fileType {
        case .avatar, .image: return "png"
        case .video: return "mp4"
        case .gif: return "gif"
        case .document: return "bin"
        }
    }
}

/// Простой семафор для асинхронного кода
actor AsyncSemaphore {
    private var count: Int
    private var waiters: [CheckedContinuation<Void, Never>] = []

    init(value: Int) {
        self.count = value
    }

    func wait() async {
        if count > 0 {
            count -= 1
            return
        }

        await withCheckedContinuation { continuation in
            waiters.append(continuation)
        }
    }

    func signal() {
        if let waiter = waiters.first {
            waiters.removeFirst()
            waiter.resume()
        } else {
            count += 1
        }
    }
}
```

---

## 3. Компоненты (подробно)

### 3.1 AttachmentGridView (NEW)

**Ответственность:** Отображение нескольких вложений в сетке

**Логика отображения:**
- 1 вложение: на всю ширину (с ограничением)
- 2 вложения: 2 колонки, равная высота
- 3 вложения: 1 большое + 2 маленьких
- 4+ вложений: сетка 2x2, остальные под катом с "+N"

```swift
struct AttachmentGridView: View {
    let attachments: [MessageAttachment]
    let isOwn: Bool
    let onTap: (MessageAttachment) -> Void
    let onLongPress: (MessageAttachment) -> Void

    // Константы
    private let maxWidth: CGFloat = 300
    private let maxHeight: CGFloat = 300
    private let spacing: CGFloat = 2

    var body: some View {
        switch attachments.count {
        case 0:
            EmptyView()
        case 1:
            singleAttachmentView(attachments[0])
        case 2:
            twoAttachmentsView(attachments)
        case 3:
            threeAttachmentsView(attachments)
        default:
            gridAttachmentsView(attachments)
        }
    }

    // MARK: - Layout Methods

    @ViewBuilder
    private func singleAttachmentView(_ attachment: MessageAttachment) -> some View {
        attachmentView(attachment, size: CGSize(width: maxWidth, height: maxHeight))
    }

    @ViewBuilder
    private func twoAttachmentsView(_ attachments: [MessageAttachment]) -> some View {
        HStack(spacing: spacing) {
            ForEach(attachments) { attachment in
                attachmentView(
                    attachment,
                    size: CGSize(width: (maxWidth - spacing) / 2, height: maxHeight / 2)
                )
            }
        }
    }

    @ViewBuilder
    private func threeAttachmentsView(_ attachments: [MessageAttachment]) -> some View {
        HStack(spacing: spacing) {
            // Большое слева
            attachmentView(
                attachments[0],
                size: CGSize(width: (maxWidth - spacing) * 0.6, height: maxHeight)
            )

            // Два маленьких справа
            VStack(spacing: spacing) {
                attachmentView(
                    attachments[1],
                    size: CGSize(width: (maxWidth - spacing) * 0.4, height: (maxHeight - spacing) / 2)
                )
                attachmentView(
                    attachments[2],
                    size: CGSize(width: (maxWidth - spacing) * 0.4, height: (maxHeight - spacing) / 2)
                )
            }
        }
    }

    @ViewBuilder
    private func gridAttachmentsView(_ attachments: [MessageAttachment]) -> some View {
        // Сетка 2x2 с индикатором "+N" для оставшихся
        let displayed = Array(attachments.prefix(4))
        let remaining = attachments.count - 4

        LazyVGrid(
            columns: [
                GridItem(.flexible(), spacing: spacing),
                GridItem(.flexible(), spacing: spacing)
            ],
            spacing: spacing
        ) {
            ForEach(Array(displayed.enumerated()), id: \.element.id) { index, attachment in
                attachmentView(
                    attachment,
                    size: CGSize(width: (maxWidth - spacing) / 2, height: maxHeight / 2)
                )
                // Для последнего показываем "+N"
                .overlay {
                    if index == 3 && remaining > 0 {
                        ZStack {
                            Color.black.opacity(0.6)
                            Text("+\(remaining)")
                                .font(.title)
                                .fontWeight(.semibold)
                                .foregroundStyle(.white)
                        }
                        .onTapGesture {
                            // Открыть галерею
                        }
                    }
                }
            }
        }
    }

    @ViewBuilder
    private func attachmentView(_ attachment: MessageAttachment, size: CGSize) -> some View {
        switch attachment.type {
        case .image:
            ImageAttachmentView(attachment: attachment, targetSize: size)
                .onTapGesture { onTap(attachment) }
        case .video:
            VideoAttachmentView(attachment: attachment, targetSize: size)
                .onTapGesture { onTap(attachment) }
        case .gif:
            GIFAttachmentView(attachment: attachment, targetSize: size)
                .onTapGesture { onTap(attachment) }
        case .document:
            DocumentAttachmentView(attachment: attachment)
                .onTapGesture { onTap(attachment) }
        case .audio:
            AudioAttachmentView(attachment: attachment)
                .onTapGesture { onTap(attachment) }
        }
    }
}
```

---

### 3.2 ImageAttachmentView (NEW)

**Ответственность:** Отображение изображения с ленивой загрузкой

**Приоритет превью (как в WPF ImageMessageContent.xaml.cs):**
1. `previewFileID` - если не пустой
2. `fileID` - fallback
3. `previewURL` - опциональный URL

```swift
struct ImageAttachmentView: View {
    let attachment: MessageAttachment
    let targetSize: CGSize

    @State private var isLoading = true
    @State private var loadError = false
    @Environment(FileCacheService.self) private var cacheService

    /// Приоритет: previewFileID > fileID (как в WPF)
    private var effectiveFileID: String {
        !attachment.previewFileID.isEmpty ? attachment.previewFileID : attachment.fileID
    }

    /// URL для загрузки изображения
    private var imageURL: URL? {
        // Если есть previewURL - используем его
        if let previewURL = attachment.previewURL, !previewURL.isEmpty {
            return URL(string: previewURL)
        }
        // Иначе получаем через FileCacheService
        return nil
    }

    var body: some View {
        ZStack {
            // Сначала проверяем previewURL
            if let url = imageURL {
                LazyImage(url: url) { state in
                    switch state {
                    case .success(let image):
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                            .onAppear { isLoading = false }
                    case .failure:
                        // Пробуем загрузить через FileCacheService
                        cachedImageView
                    case .loading:
                        loadingPlaceholder
                    }
                }
                .processors([
                    .resize(size: targetSize),
                    .roundedCorners(radius: 12)
                ])
            } else {
                // Загрузка через FileCacheService
                cachedImageView
            }
        }
        .frame(width: targetSize.width, height: targetSize.height)
        .clipShape(RoundedRectangle(cornerRadius: 12))
        .clipped()
    }

    /// Отображение через кешированный файл
    @ViewBuilder
    private var cachedImageView: some View {
        let cachedPath = cacheService.getCachedFilePath(
            fileID: effectiveFileID,
            fileType: attachment.type == .gif ? .gif : .image,
            providedURL: attachment.previewURL
        )

        if FileCacheService.isPlaceholder(url: cachedPath) {
            loadingPlaceholder
        } else {
            AsyncImage(url: cachedPath) { phase in
                switch phase {
                case .success(let image):
                    image
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                case .failure:
                    errorPlaceholder
                case .empty:
                    loadingPlaceholder
                @unknown default:
                    placeholder
                }
            }
        }
    }

    private var loadingPlaceholder: some View {
        RoundedRectangle(cornerRadius: 12)
            .fill(.fill.tertiary)
            .overlay {
                ProgressView()
                    .scaleEffect(0.8)
            }
    }

    private var errorPlaceholder: some View {
        RoundedRectangle(cornerRadius: 12)
            .fill(.fill.tertiary)
            .overlay {
                VStack(spacing: 8) {
                    Image(systemName: "photo")
                        .font(.title)
                        .foregroundStyle(.secondary)
                    Text("Ошибка загрузки")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
    }

    private var placeholder: some View {
        RoundedRectangle(cornerRadius: 12)
            .fill(.fill.tertiary)
            .overlay {
                Image(systemName: "photo")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)
            }
    }
}
```

---

### 3.3 VideoAttachmentView (NEW)

**Ответственность:** Отображение видео с превью и кнопкой воспроизведения

```swift
struct VideoAttachmentView: View {
    let attachment: MessageAttachment
    let targetSize: CGSize

    @State private var thumbnailImage: Image?
    @State private var duration: String?

    var body: some View {
        ZStack {
            // Превью видео
            if let thumbnailImage {
                thumbnailImage
                    .resizable()
                    .aspectRatio(contentMode: .fill)
            } else {
                previewPlaceholder
            }

            // Кнопка воспроизведения
            playButton

            // Длительность (если есть)
            if let duration {
                durationBadge(duration)
            }
        }
        .frame(width: targetSize.width, height: targetSize.height)
        .clipShape(RoundedRectangle(cornerRadius: 12))
        .clipped()
        .task {
            await loadThumbnail()
        }
    }

    private var previewPlaceholder: some View {
        RoundedRectangle(cornerRadius: 12)
            .fill(.fill.tertiary)
    }

    private var playButton: some View {
        Circle()
            .fill(.black.opacity(0.5))
            .frame(width: 50, height: 50)
            .overlay {
                Image(systemName: "play.fill")
                    .font(.title2)
                    .foregroundStyle(.white)
                    .offset(x: 2) // Визуальное центрирование
            }
    }

    private func durationBadge(_ duration: String) -> some View {
        Text(duration)
            .font(.caption2)
            .fontWeight(.medium)
            .foregroundStyle(.white)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(.black.opacity(0.6))
            .clipShape(Capsule())
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomTrailing)
            .padding(8)
    }

    private func loadThumbnail() async {
        // Загрузка превью через AVAssetImageGenerator
        // или использование previewURL из attachment
    }
}
```

---

### 3.4 GIFAttachmentView (NEW)

**Ответственность:** Отображение GIF-анимации

```swift
struct GIFAttachmentView: View {
    let attachment: MessageAttachment
    let targetSize: CGSize

    var body: some View {
        // GIF аналогично изображению, но с поддержкой анимации
        // Nuke поддерживает GIF через Gifu или FLAnimatedImage

        ZStack {
            if let previewURL = attachment.previewURL,
               let url = URL(string: previewURL) {
                // Используем AnimatedImage из NukeUI
                LazyImage(url: url) { state in
                    if let image = state.image {
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                    } else {
                        placeholder
                    }
                }
            } else {
                placeholder
            }

            // Метка "GIF"
            gifBadge
        }
        .frame(width: targetSize.width, height: targetSize.height)
        .clipShape(RoundedRectangle(cornerRadius: 12))
        .clipped()
    }

    private var placeholder: some View {
        RoundedRectangle(cornerRadius: 12)
            .fill(.fill.tertiary)
            .overlay {
                ProgressView()
            }
    }

    private var gifBadge: some View {
        Text("GIF")
            .font(.caption2)
            .fontWeight(.bold)
            .foregroundStyle(.white)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(.black.opacity(0.6))
            .clipShape(Capsule())
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .padding(8)
    }
}
```

---

### 3.5 DocumentAttachmentView (NEW)

**Ответственность:** Отображение документа с иконкой и информацией

```swift
struct DocumentAttachmentView: View {
    let attachment: MessageAttachment

    var body: some View {
        HStack(spacing: 12) {
            // Иконка файла
            FileIconView(fileExtension: fileExtension)
                .frame(width: 44, height: 44)

            // Информация о файле
            VStack(alignment: .leading, spacing: 4) {
                Text(attachment.fileName)
                    .font(.subheadline)
                    .lineLimit(2)
                    .truncationMode(.middle)

                Text(attachment.formattedSize)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Spacer()

            // Кнопка скачивания
            Button {
                // Скачать файл
            } label: {
                Image(systemName: "arrow.down.circle")
                    .font(.title2)
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
        }
        .padding(12)
        .background(.fill.tertiary)
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }

    private var fileExtension: String {
        URL(fileURLWithPath: attachment.fileName).pathExtension
    }
}
```

---

### 3.6 FileIconView (NEW)

**Ответственность:** Иконка для типа файла

```swift
struct FileIconView: View {
    let fileExtension: String

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 8)
                .fill(iconColor.opacity(0.2))

            Image(systemName: iconSystemName)
                .font(.title2)
                .foregroundStyle(iconColor)
        }
    }

    private var iconColor: Color {
        switch fileExtension.lowercased() {
        case "pdf": return .red
        case "doc", "docx": return .blue
        case "xls", "xlsx": return .green
        case "ppt", "pptx": return .orange
        case "zip", "rar", "7z": return .purple
        case "mp3", "wav", "m4a": return .pink
        default: return .gray
        }
    }

    private var iconSystemName: String {
        switch fileExtension.lowercased() {
        case "pdf": return "doc.richtext"
        case "doc", "docx": return "doc.text"
        case "xls", "xlsx": return "tablecells"
        case "ppt", "pptx": return "play.rectangle"
        case "zip", "rar", "7z": return "doc.zipper"
        case "mp3", "wav", "m4a": return "music.note"
        case "txt": return "doc.plaintext"
        default: return "doc"
        }
    }
}
```

---

### 3.7 MediaViewerView (NEW)

**Ответственность:** Полноэкранный просмотр медиа

```swift
struct MediaViewerView: View {
    let attachments: [MessageAttachment]
    let initialIndex: Int
    @Environment(\.dismiss) private var dismiss

    @State private var currentIndex: Int
    @State private var isControlsVisible = true

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            TabView(selection: $currentIndex) {
                ForEach(Array(attachments.enumerated()), id: \.element.id) { index, attachment in
                    viewerContent(for: attachment)
                        .tag(index)
                }
            }
            .tabViewStyle(.page(indexDisplayMode: .automatic))

            // Controls overlay
            if isControlsVisible {
                controlsOverlay
            }
        }
        .onTapGesture {
            withAnimation { isControlsVisible.toggle() }
        }
        .gesture(
            DragGesture()
                .onEnded { value in
                    if value.translation.height > 100 {
                        dismiss()
                    }
                }
        )
    }

    @ViewBuilder
    private func viewerContent(for attachment: MessageAttachment) -> some View {
        switch attachment.type {
        case .image, .gif:
            ImageViewerView(attachment: attachment)
        case .video:
            VideoPlayerView(attachment: attachment)
        default:
            DocumentPreviewView(attachment: attachment)
        }
    }

    private var controlsOverlay: some View {
        VStack {
            // Header
            HStack {
                Button {
                    dismiss()
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.title)
                        .foregroundStyle(.white)
                }

                Spacer()

                Text("\(currentIndex + 1) / \(attachments.count)")
                    .font(.subheadline)
                    .foregroundStyle(.white)

                Spacer()

                Button {
                    // Share
                } label: {
                    Image(systemName: "square.and.arrow.up")
                        .font(.title2)
                        .foregroundStyle(.white)
                }
            }
            .padding()

            Spacer()

            // Footer with file info
            if let attachment = attachments[safe: currentIndex] {
                HStack {
                    VStack(alignment: .leading) {
                        Text(attachment.fileName)
                            .font(.headline)
                            .foregroundStyle(.white)
                        Text(attachment.formattedSize)
                            .font(.caption)
                            .foregroundStyle(.white.opacity(0.7))
                    }
                    Spacer()
                }
                .padding()
            }
        }
        .transition(.opacity)
    }
}

// Safe array access
extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}
```

---

### 3.8 ImageViewerView (NEW)

**Ответственность:** Просмотр изображения с зумом

```swift
struct ImageViewerView: View {
    let attachment: MessageAttachment

    @State private var scale: CGFloat = 1.0
    @State private var lastScale: CGFloat = 1.0
    @State private var offset: CGSize = .zero
    @State private var lastOffset: CGSize = .zero

    var body: some View {
        GeometryReader { geometry in
            if let previewURL = attachment.previewURL ?? attachment.previewFileID,
               let url = URL(string: previewURL) {
                AsyncImage(url: url) { phase in
                    switch phase {
                    case .success(let image):
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fit)
                            .scaleEffect(scale)
                            .offset(offset)
                            .gesture(
                                SimultaneousGesture(
                                    MagnificationGesture()
                                        .onChanged { value in
                                            scale = lastScale * value
                                        }
                                        .onEnded { _ in
                                            withAnimation {
                                                scale = min(max(scale, 1), 4)
                                                lastScale = scale
                                            }
                                        },
                                    DragGesture()
                                        .onChanged { value in
                                            offset = CGSize(
                                                width: lastOffset.width + value.translation.width,
                                                height: lastOffset.height + value.translation.height
                                            )
                                        }
                                        .onEnded { _ in
                                            withAnimation {
                                                offset = .zero
                                                lastOffset = .zero
                                            }
                                        }
                                )
                            )
                            .onTapGesture(count: 2) {
                                withAnimation {
                                    if scale > 1 {
                                        scale = 1
                                        offset = .zero
                                    } else {
                                        scale = 2
                                    }
                                    lastScale = scale
                                }
                            }
                    default:
                        ProgressView()
                            .foregroundStyle(.white)
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
    }
}
```

---

### 3.9 VideoPlayerView (NEW)

**Ответственность:** Видеоплеер с контролами

```swift
struct VideoPlayerView: View {
    let attachment: MessageAttachment
    @State private var player: AVPlayer?
    @State private var isPlaying = false

    var body: some View {
        ZStack {
            if let player {
                VideoPlayer(player: player)
                    .ignoresSafeArea()
            } else {
                ProgressView()
                    .foregroundStyle(.white)
            }
        }
        .task {
            await loadVideo()
        }
        .onDisappear {
            player?.pause()
        }
    }

    private func loadVideo() async {
        // Получить URL видео через FileService
        // Создать AVPlayer
    }
}
```

---

## 4. Интеграция с MessageBubbleView

### 4.1 Модификация MessageBubbleView

```swift
struct MessageBubbleView: View {
    // ... существующие свойства

    @ViewBuilder
    private var bubbleContent: some View {
        VStack(alignment: isOwn ? .trailing : .leading, spacing: 4) {
            // Вложения (если есть) - ПЕРЕД текстом
            if !message.content.attachments.isEmpty {
                AttachmentGridView(
                    attachments: message.content.attachments,
                    isOwn: isOwn,
                    onTap: { attachment in
                        // Открыть просмотрщик
                        showMediaViewer(for: attachment)
                    },
                    onLongPress: { attachment in
                        // Контекстное меню
                    }
                )
            }

            // Текст сообщения
            if !message.content.text.isEmpty {
                Text(message.content.text)
                    .font(.body)
                    .foregroundStyle(isOwn ? .white : .primary)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 8)
            }
        }
    }

    private func showMediaViewer(for attachment: MessageAttachment) {
        // Найти индекс вложения
        // Открыть MediaViewerView
    }
}
```

---

## 5. FileService Integration

### 5.1 Получение URL для просмотра/скачивания

```swift
// В AttachmentViewModel или напрямую
func getDownloadURL(for attachment: MessageAttachment) async -> URL? {
    do {
        let urls = try await fileService.getTempDownloadURLs(
            fileIDs: [attachment.fileID]
        )
        return urls.first?.url
    } catch {
        return nil
    }
}
```

---

## 6. Требования к производительности

### 6.1 Ленивая загрузка
- Изображения загружаются только при появлении на экране
- Превью загружаются первым, полноразмерные - по требованию
- Кэширование через Nuke

### 6.2 Оптимизация памяти
- Ограничение размера кэша изображений
- Освобождение ресурсов при закрытии просмотрщика

### 6.3 Видео
- Streaming вместо полной загрузки
- Автоматическое освобождение плеера

---

## 7. Accessibility

- VoiceOver: описание типа вложения ("изображение", "видео 2 минуты")
- VoiceOver: озвучивание имени и размера файла
- Поддержка VoiceOver для кнопок воспроизведения/скачивания

---

## 8. Тестирование

### 8.1 Unit тесты
- `AttachmentLayoutCalculator` - расчёт размеров сетки
- `FileIconProvider` - правильные иконки для типов

### 8.2 UI тесты
- Отображение одного изображения
- Отображение сетки 2x2
- Открытие и закрытие просмотрщика
- Зум изображений

---

## 9. Критерии приёмки

- [ ] Изображения отображаются с превью и правильным соотношением сторон
- [ ] Видео отображаются с превью, кнопкой воспроизведения и длительностью
- [ ] GIF-анимации воспроизводятся корректно
- [ ] Документы отображаются с иконкой, именем и размером
- [ ] Сетка для нескольких вложений работает корректно (1, 2, 3, 4+)
- [ ] Просмотрщик медиа открывается и закрывается
- [ ] Зум изображений работает (pinch + double tap)
- [ ] Видеоплеер воспроизводит видео
- [ ] Нет утечек памяти при просмотре медиа

---

## 10. Связанные файлы

### Существующие (модифицировать)
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/MessageAttachmentView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/MessageBubbleView.swift`

### Новые (создать)
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Attachments/AttachmentGridView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Attachments/ImageAttachmentView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Attachments/VideoAttachmentView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Attachments/GIFAttachmentView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Attachments/DocumentAttachmentView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Attachments/FileIconView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Viewers/MediaViewerView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Viewers/ImageViewerView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Viewers/VideoPlayerView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Helpers/AttachmentLayoutCalculator.swift`

---

## 11. Примечания

- Для GIF может потребоваться дополнительная зависимость (Gifu)
- Видео плеер использует AVKit (нативный)
- Использовать существующий `FileService` из BFCore
- Цвета иконок файлов можно настроить в `Theme.swift`
