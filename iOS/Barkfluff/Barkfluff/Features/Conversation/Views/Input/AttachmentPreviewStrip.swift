//
//  AttachmentPreviewStrip.swift
//  Barkfluff
//
//  Полоса превью выбранных вложений (iOS версия)
//

import SwiftUI
import PhotosUI

/// Полоса превью выбранных вложений
struct AttachmentPreviewStrip: View {
    @Binding var attachments: [SelectedAttachment]
    let uploadProgress: [UUID: Double]
    let onPickMedia: () -> Void
    let onPickDocument: () -> Void

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: Theme.Spacing.sm) {
                ForEach(attachments) { attachment in
                    AttachmentPreviewCard(
                        attachment: attachment,
                        progress: uploadProgress[attachment.id],
                        onRemove: {
                            withAnimation(.spring(duration: 0.3)) {
                                attachments.removeAll { $0.id == attachment.id }
                            }
                        }
                    )
                }

                // Кнопка добавления
                addMoreButton
            }
            .padding(.horizontal, 4)
        }
    }

    private var addMoreButton: some View {
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
            VStack {
                Image(systemName: "plus")
                    .font(.title2)
                    .foregroundStyle(.secondary)
            }
            .frame(width: 60, height: 60)
            .background(Color(uiColor: .secondarySystemGroupedBackground))
            .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.md))
        }
    }
}

// MARK: - Attachment Preview Card

struct AttachmentPreviewCard: View {
    let attachment: SelectedAttachment
    let progress: Double?
    let onRemove: () -> Void

    var body: some View {
        ZStack(alignment: .topTrailing) {
            // Превью
            previewContent
                .frame(width: 60, height: 60)
                .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.md))

            // Прогресс загрузки
            if let progress, progress < 1.0 {
                RoundedRectangle(cornerRadius: Theme.Radius.md)
                    .fill(Color.black.opacity(0.4))
                    .frame(width: 60, height: 60)
                    .overlay {
                        ProgressView(value: progress)
                            .progressViewStyle(.circular)
                            .tint(.white)
                    }
            }

            // Кнопка удаления (без смещения, внутри bounds)
            Button {
                onRemove()
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .font(.system(size: 18))
                    .symbolRenderingMode(.palette)
                    .foregroundStyle(.white, .gray.opacity(0.8))
            }
            .offset(x: 6, y: -6)
        }
        .padding(.top, 8) // Добавляем место сверху для кнопки удаления
        }

    @ViewBuilder
    private var previewContent: some View {
        if let image = attachment.previewImage {
            Image(uiImage: image)
                .resizable()
                .aspectRatio(contentMode: .fill)
        } else if attachment.isVideo {
            ZStack {
                Color(uiColor: .secondarySystemGroupedBackground)
                Image(systemName: "video.fill")
                    .font(.title2)
                    .foregroundStyle(.secondary)
            }
        } else {
            ZStack {
                Color(uiColor: .secondarySystemGroupedBackground)
                FileIconView(fileExtension: attachment.fileExtension, size: 30)
            }
        }
    }
}

#Preview {
    VStack {
        AttachmentPreviewStrip(
            attachments: .constant([
                .imageData(data: Data(), fileName: "photo1.jpg"),
                .imageData(data: Data(), fileName: "photo2.jpg"),
                .fileURL(url: URL(fileURLWithPath: "/tmp/doc.pdf"))
            ]),
            uploadProgress: [:],
            onPickMedia: {},
            onPickDocument: {}
        )

        AttachmentPreviewStrip(
            attachments: .constant([
                .imageData(data: Data(), fileName: "photo1.jpg")
            ]),
            uploadProgress: [UUID(): 0.5],
            onPickMedia: {},
            onPickDocument: {}
        )
    }
    .padding()
}
