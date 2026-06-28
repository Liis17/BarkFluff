//
//  MessageInputView.swift
//  Barkfluff
//
//  Поле ввода сообщения с поддержкой вложений
//  Парит поверх сообщений с градиентным blur-эффектом
//

import SwiftUI
import UniformTypeIdentifiers
import AppKit
import BFCore

struct MessageInputView: View {
    @Binding var text: String
    @Binding var selectedAttachments: [SelectedAttachment]
    let isSending: Bool
    let uploadProgress: [UUID: Double]
    /// Режим редактирования — кнопка отправки показывается как галочка
    var isEditMode: Bool = false
    /// Сообщение, на которое формируется ответ. Превью отрисовывается внутри composer
    /// (под градиентом, плотно к инпуту), чтобы между ним и текстовым полем не было зазора.
    var pendingReply: Message? = nil
    /// Сообщение, которое сейчас редактируется. Превью отрисовывается так же, как pendingReply.
    var editingMessage: Message? = nil
    /// Сброс reply (обычно — viewModel.clearPendingReply()).
    var onCancelReply: (() -> Void)? = nil
    /// Сброс edit (обычно — viewModel.cancelEdit() + очистка messageText).
    var onCancelEdit: (() -> Void)? = nil
    let onSend: () -> Void
    let onFileSelected: ([URL], Bool) -> Void  // URLs, forceAsDocument
    /// Сервис стикеров — нужен пикеру.
    let stickersService: StickersServiceProtocol
    /// Хранилище недавно использованных стикеров.
    let recentStickersStore: RecentStickersStore
    /// Колбэк выбора стикера в пикере.
    let onStickerSelected: (Sticker) -> Void

    @State private var showEmojiPicker = false
    @State private var showStickerPicker = false
    @FocusState private var isTextFieldFocused: Bool
    @Environment(\.locale) private var locale

    var body: some View {
        VStack(spacing: 0) {
            // Reply-превью — плотно над инпутом, в области градиента composer'а.
            if let reply = pendingReply {
                ReplyPreviewView(
                    authorName: reply.senderName ?? String(localized: "common.unknown_user"),
                    snippet: ReplyPreviewView.makeSnippet(reply, locale: locale),
                    onCancel: { onCancelReply?() }
                )
                .transition(.asymmetric(
                    insertion: .move(edge: .bottom).combined(with: .opacity),
                    removal: .opacity
                ))
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.top, Theme.Spacing.sm)
            }

            // Edit-превью — то же место, что у reply (взаимоисключающие).
            if let editing = editingMessage {
                EditPreviewView(
                    snippet: ReplyPreviewView.makeSnippet(editing, locale: locale),
                    onCancel: { onCancelEdit?() }
                )
                .transition(.asymmetric(
                    insertion: .move(edge: .bottom).combined(with: .opacity),
                    removal: .opacity
                ))
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.top, Theme.Spacing.sm)
            }

            // Полоса превью вложений (только если есть)
            if !selectedAttachments.isEmpty {
                AttachmentPreviewStrip(
                    attachments: $selectedAttachments,
                    uploadProgress: uploadProgress,
                    onPickMedia: { openMediaPicker() },
                    onPickDocument: { openDocumentPicker() }
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
                // Кнопка прикрепления (скрепка, синяя)
                // Левый клик - общий пикер, правый - меню с выбором типа
                Button {
                    openFilePicker()
                } label: {
                    Image(systemName: "paperclip")
                        .font(.title3)
                        .fontWeight(.semibold)
                        .foregroundStyle(.white)
                        .frame(width: 32, height: 32)
                        .background(
                            Circle()
                                .fill(Color.accentColor)
                        )
                }
                .buttonStyle(.plain)
                .contextMenu {
                    Button {
                        openMediaPicker()
                    } label: {
                        Label("conversation.attach.photo_or_video", systemImage: "photo.on.rectangle")
                    }

                    Divider()

                    Button {
                        openDocumentPicker()
                    } label: {
                        Label("conversation.attach.file_no_compression", systemImage: "doc")
                    }
                }

                // Поле ввода текста - Capsule с Liquid Glass + кнопка эмодзи
                HStack(alignment: .center, spacing: 4) {
                    TextField("conversation.input.placeholder", text: $text, axis: .vertical)
                        .textFieldStyle(.plain)
                        .lineLimit(1...6)
                        .focused($isTextFieldFocused)
                        .onSubmit { onSend() }

                    // Кнопка выбора эмодзи
                    Button {
                        showEmojiPicker.toggle()
                    } label: {
                        Image(systemName: "face.smiling")
                            .font(.system(size: 18))
                            .foregroundStyle(.secondary)
                    }
                    .buttonStyle(.plain)
                    .popover(isPresented: $showEmojiPicker, arrowEdge: .bottom) {
                        EmojiPickerView { emoji in
                            text.append(emoji)
                        }
                    }

                    // Кнопка выбора стикера
                    Button {
                        showStickerPicker.toggle()
                    } label: {
                        Image(systemName: "square.grid.2x2.fill")
                            .font(.system(size: 18))
                            .foregroundStyle(.secondary)
                    }
                    .buttonStyle(.plain)
                    .popover(isPresented: $showStickerPicker, arrowEdge: .bottom) {
                        StickerPickerView(
                            service: stickersService,
                            recentStore: recentStickersStore
                        ) { sticker in
                            showStickerPicker = false
                            onStickerSelected(sticker)
                        }
                    }
                }
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.vertical, Theme.Spacing.sm)
                .glassEffect(.regular, in: .capsule)

                // Кнопка отправки
                SendButton(
                    canSend: canSend,
                    isSending: isSending,
                    isEditMode: isEditMode,
                    action: onSend
                )
            }
            .padding(.horizontal, Theme.Spacing.md)
            .padding(.vertical, Theme.Spacing.sm)
        }
        .padding(.top, 30)  // Пространство для градиента
        .background {
            // Градиентный fade
            Rectangle()
                .fill(
                    LinearGradient(
                        gradient: Gradient(stops: [
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0), location: 0),
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0.4), location: 0.3),
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0.7), location: 0.6),
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0.9), location: 1.0)
                        ]),
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )
        }
        .animation(.spring(duration: 0.3), value: selectedAttachments.count)
    }

    // MARK: - Computed

    private var canSend: Bool {
        !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || !selectedAttachments.isEmpty
    }

    // MARK: - File Pickers

    private func openFilePicker() {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        panel.canChooseFiles = true
        panel.message = String(localized: "conversation.attach.picker.files_message")
        panel.allowedContentTypes = [.item]

        if panel.runModal() == .OK {
            let urls = panel.urls
            if !urls.isEmpty {
                // Определяем тип автоматически по расширению
                onFileSelected(urls, false)
            }
        }
    }

    private func openMediaPicker() {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        panel.canChooseFiles = true
        panel.message = String(localized: "conversation.attach.picker.media_message")
        panel.allowedContentTypes = [.image, .movie]

        if panel.runModal() == .OK {
            let urls = panel.urls
            if !urls.isEmpty {
                onFileSelected(urls, false)
            }
        }
    }

    private func openDocumentPicker() {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        panel.canChooseFiles = true
        panel.message = String(localized: "conversation.attach.picker.file_message")
        panel.allowedContentTypes = [.item]

        if panel.runModal() == .OK {
            let urls = panel.urls
            if !urls.isEmpty {
                // Как документ - без сжатия
                onFileSelected(urls, true)
            }
        }
    }
}
