//
//  MessageInputView.swift
//  Barkfluff
//
//  Поле ввода сообщения с поддержкой вложений (iOS версия)
//  Использует Liquid Glass дизайн для iOS 26
//

import SwiftUI
import PhotosUI

struct MessageInputView: View {
    @Binding var text: String
    @Binding var selectedAttachments: [SelectedAttachment]
    let isSending: Bool
    let uploadProgress: [UUID: Double]
    let onSend: () -> Void
    let onFileSelected: ([URL], Bool) -> Void  // URLs, forceAsDocument
    let onStickerTap: () -> Void

    @FocusState private var isTextFieldFocused: Bool
    @State private var showPhotoPicker = false
    @State private var showDocumentPicker = false
    @State private var selectedPhotoItems: [PhotosPickerItem] = []

    var body: some View {
        VStack(spacing: 0) {
            // Полоса превью вложений
            if !selectedAttachments.isEmpty {
                AttachmentPreviewStrip(
                    attachments: $selectedAttachments,
                    uploadProgress: uploadProgress,
                    onPickMedia: { showPhotoPicker = true },
                    onPickDocument: { showDocumentPicker = true }
                )
                .transition(.asymmetric(
                    insertion: .move(edge: .bottom).combined(with: .opacity),
                    removal: .opacity
                ))
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.top, Theme.Spacing.sm)
            }

            // Основная строка ввода
            HStack(alignment: .center, spacing: Theme.Spacing.sm) {
                // Кнопка прикрепления
                attachButton

                // Кнопка стикеров
                stickerButton

                // Поле ввода текста с Liquid Glass
                textFieldView

                // Кнопка отправки
                sendButton
            }
            .padding(.horizontal, Theme.Spacing.md)
            .padding(.vertical, Theme.Spacing.sm)
        }
        .background {
            VStack(spacing: 0) {
                // Плавный градиент сверху для размытия контента
                Rectangle()
                    .fill(.ultraThinMaterial)
                    .mask(
                        LinearGradient(
                            gradient: Gradient(stops: [
                                .init(color: .black.opacity(0), location: 0),
                                .init(color: .black.opacity(0.3), location: 0.3),
                                .init(color: .black.opacity(0.7), location: 0.6),
                                .init(color: .black.opacity(0.95), location: 0.85),
                                .init(color: .black, location: 1.0)
                            ]),
                            startPoint: .top,
                            endPoint: .bottom
                        )
                    )
                    .frame(height: 50)

                // Сплошной material под полем ввода
                Rectangle()
                    .fill(.ultraThinMaterial)
            }
        }
        .animation(.spring(duration: 0.3), value: selectedAttachments.count)
        .photosPicker(isPresented: $showPhotoPicker, selection: $selectedPhotoItems, maxSelectionCount: 10, matching: .any(of: [.images, .videos]))
        .onChange(of: selectedPhotoItems) { _, newItems in
            processPhotoItems(newItems)
        }
        .fileImporter(
            isPresented: $showDocumentPicker,
            allowedContentTypes: [.item],
            allowsMultipleSelection: true
        ) { result in
            switch result {
            case .success(let urls):
                onFileSelected(urls, true)
            case .failure:
                break
            }
        }
    }

    // MARK: - Attach Button

    private var attachButton: some View {
        Menu {
            Button {
                showPhotoPicker = true
            } label: {
                Label("Фото или видео", systemImage: "photo.on.rectangle")
            }

            Button {
                showDocumentPicker = true
            } label: {
                Label("Файл", systemImage: "doc")
            }

            // Camera (if available)
            #if targetEnvironment(simulator)
            #else
            Divider()

            Button {
                // Camera action handled separately
            } label: {
                Label("Камера", systemImage: "camera")
            }
            #endif
        } label: {
            Image(systemName: "paperclip")
                .font(.title2)
                .foregroundStyle(Color.accentColor)
        }
        .buttonStyle(.plain)
    }

    // MARK: - Sticker Button

    private var stickerButton: some View {
        Button {
            onStickerTap()
        } label: {
            Image(systemName: "face.smiling")
                .font(.title2)
                .foregroundStyle(Color.accentColor)
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Стикеры")
    }

    // MARK: - Text Field

    private var textFieldView: some View {
        HStack(alignment: .center, spacing: Theme.Spacing.xs) {
            TextField("Сообщение...", text: $text, axis: .vertical)
                .textFieldStyle(.plain)
                .lineLimit(1...6)
                .focused($isTextFieldFocused)
                .onSubmit {
                    if !canSend { return }
                    onSend()
                }

            // Emoji button (optional - iOS has built-in emoji keyboard)
        }
        .padding(.horizontal, Theme.Spacing.md)
        .padding(.vertical, Theme.Spacing.sm)
        .glassEffect(.regular, in: Capsule())
    }

    // MARK: - Send Button

    private var sendButton: some View {
        Button {
            guard canSend else { return }
            onSend()
        } label: {
            if isSending {
                ProgressView()
                    .frame(width: 32, height: 32)
            } else {
                Image(systemName: "arrow.up.circle.fill")
                    .font(.system(size: 32))
                    .foregroundStyle(canSend ? Color.accentColor : Color(uiColor: .systemGray3))
            }
        }
        .buttonStyle(.plain)
        .disabled(!canSend && !isSending)
    }

    // MARK: - Computed

    private var canSend: Bool {
        !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || !selectedAttachments.isEmpty
    }

    // MARK: - Photo Processing

    private func processPhotoItems(_ items: [PhotosPickerItem]) {
        guard !items.isEmpty else { return }

        Task {
            for item in items {
                // Пытаемся загрузить как изображение
                if let data = try? await item.loadTransferable(type: Data.self),
                   let image = UIImage(data: data) {
                    let fileName = "photo_\(Int(Date().timeIntervalSince1970)).jpg"
                    await MainActor.run {
                        selectedAttachments.append(.imageData(data: data, fileName: fileName))
                    }
                    continue
                }

                // Пытаемся загрузить как видео URL
                if let movie = try? await item.loadTransferable(type: VideoTransferable.self) {
                    // Генерируем превью для видео
                    let asset = AVURLAsset(url: movie.url)
                    let generator = AVAssetImageGenerator(asset: asset)
                    generator.appliesPreferredTrackTransform = true
                    var preview: UIImage?
                    if let cgImage = try? generator.copyCGImage(at: .zero, actualTime: nil) {
                        preview = UIImage(cgImage: cgImage)
                    }

                    let fileName = movie.url.lastPathComponent
                    let videoData = (try? Data(contentsOf: movie.url)) ?? Data()

                    await MainActor.run {
                        selectedAttachments.append(.videoData(data: videoData, preview: preview, fileName: fileName))
                    }
                }
            }
        }

        // Очищаем выбранные items
        selectedPhotoItems = []
    }
}

// MARK: - Video Transferable

import AVFoundation
import UniformTypeIdentifiers

struct VideoTransferable: Transferable {
    let url: URL

    static var transferRepresentation: some TransferRepresentation {
        FileRepresentation(importedContentType: .movie) { received in
            // Копируем во временную директорию
            let copy = URL(fileURLWithPath: NSTemporaryDirectory())
                .appendingPathComponent(UUID().uuidString)
                .appendingPathExtension("mov")
            try FileManager.default.copyItem(at: received.file, to: copy)
            return Self(url: copy)
        }
    }
}

#Preview {
    VStack {
        Spacer()

        MessageInputView(
            text: .constant(""),
            selectedAttachments: .constant([]),
            isSending: false,
            uploadProgress: [:],
            onSend: {},
            onFileSelected: { _, _ in },
            onStickerTap: {}
        )

        MessageInputView(
            text: .constant("Привет!"),
            selectedAttachments: .constant([]),
            isSending: false,
            uploadProgress: [:],
            onSend: {},
            onFileSelected: { _, _ in },
            onStickerTap: {}
        )
    }
    .environment(AppCoordinator())
    .environment(DependencyContainer())
}
