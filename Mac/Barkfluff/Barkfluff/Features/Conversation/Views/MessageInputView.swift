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

struct MessageInputView: View {
    @Binding var text: String
    @Binding var selectedAttachments: [SelectedAttachment]
    let isSending: Bool
    let uploadProgress: [UUID: Double]
    let onSend: () -> Void
    let onFileSelected: ([URL], Bool) -> Void  // URLs, forceAsDocument

    @State private var showEmojiPicker = false
    @FocusState private var isTextFieldFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
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
                        Label("Фото или видео", systemImage: "photo.on.rectangle")
                    }

                    Divider()

                    Button {
                        openDocumentPicker()
                    } label: {
                        Label("Файл (без сжатия)", systemImage: "doc")
                    }
                }

                // Поле ввода текста - Capsule с Liquid Glass + кнопка эмодзи
                HStack(alignment: .center, spacing: 4) {
                    TextField("Сообщение...", text: $text, axis: .vertical)
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
                }
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.vertical, Theme.Spacing.sm)
                .glassEffect(.regular, in: .capsule)

                // Кнопка отправки
                SendButton(
                    canSend: canSend,
                    isSending: isSending,
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
        panel.message = "Выберите файлы для отправки"
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
        panel.message = "Выберите фото или видео"
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
        panel.message = "Выберите файл"
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
