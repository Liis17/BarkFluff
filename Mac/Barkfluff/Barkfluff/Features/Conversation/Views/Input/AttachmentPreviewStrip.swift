//
//  AttachmentPreviewStrip.swift
//  Barkfluff
//
//  Горизонтальная полоса превью выбранных вложений
//

import SwiftUI

struct AttachmentPreviewStrip: View {
    @Binding var attachments: [SelectedAttachment]
    let uploadProgress: [UUID: Double]
    let onPickMedia: () -> Void
    let onPickDocument: () -> Void

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(attachments) { attachment in
                    if attachment.isMedia {
                        MediaPreviewCard(
                            attachment: attachment,
                            progress: uploadProgress[attachment.id],
                            onRemove: { remove(attachment) }
                        )
                    } else {
                        FilePreviewCard(
                            attachment: attachment,
                            progress: uploadProgress[attachment.id],
                            onRemove: { remove(attachment) }
                        )
                    }
                }

                // Кнопка "добавить ещё" - Menu с теми же опциями
                addMoreMenu
            }
            .padding(.vertical, 4)
        }
        .frame(height: 68)
    }

    private var addMoreMenu: some View {
        Menu {
            Button {
                onPickMedia()
            } label: {
                Label("Фото или видео", systemImage: "photo.on.rectangle")
            }

            Button {
                onPickDocument()
            } label: {
                Label("Файл", systemImage: "doc")
            }
        } label: {
            RoundedRectangle(cornerRadius: 8)
                .strokeBorder(style: StrokeStyle(lineWidth: 1, dash: [4, 3]))
                .foregroundStyle(.secondary.opacity(0.5))
                .frame(width: 56, height: 56)
                .overlay {
                    Image(systemName: "plus")
                        .font(.title3)
                        .foregroundStyle(.secondary)
                }
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
    }

    private func remove(_ attachment: SelectedAttachment) {
        withAnimation(.spring(duration: 0.25)) {
            attachments.removeAll { $0.id == attachment.id }
        }
    }
}

// MARK: - Preview

#Preview {
    struct PreviewContainer: View {
        @State private var attachments: [SelectedAttachment] = []

        var body: some View {
            VStack {
                AttachmentPreviewStrip(
                    attachments: $attachments,
                    uploadProgress: [:],
                    onPickMedia: {
                        if let testData = "test".data(using: .utf8) {
                            attachments.append(.imageData(data: testData, fileName: "photo.jpg"))
                        }
                    },
                    onPickDocument: {
                        if let testData = "test".data(using: .utf8) {
                            attachments.append(.imageData(data: testData, fileName: "document.pdf"))
                        }
                    }
                )

                Button("Add Attachment") {
                    if let testData = "test content".data(using: .utf8) {
                        attachments.append(.imageData(data: testData, fileName: "document.pdf"))
                    }
                }
                .padding()
            }
            .padding()
            .frame(width: 400)
        }
    }

    return PreviewContainer()
}
