//
//  DocumentAttachmentView.swift
//  Barkfluff
//
//  Отображение документа-вложения с иконкой по типу
//

import SwiftUI
import BFCore

struct DocumentAttachmentView: View {
    let attachment: MessageAttachment
    let onTap: () -> Void
    var uploadProgress: Double?

    private var isUploading: Bool {
        if let p = uploadProgress { return p < 1.0 }
        return false
    }

    var body: some View {
        Button(action: { if !isUploading { onTap() } }) {
            HStack(spacing: 12) {
                // Иконка файла
                FileIconView(fileName: attachment.fileName, size: 44, backgroundColor: .white.opacity(0.15))

                // Информация о файле
                VStack(alignment: .leading, spacing: 4) {
                    Text(attachment.fileName)
                        .font(.subheadline)
                        .lineLimit(2)
                        .foregroundStyle(.white)
                        .multilineTextAlignment(.leading)

                    if isUploading, let progress = uploadProgress {
                        ProgressView(value: progress)
                            .progressViewStyle(.linear)
                            .tint(.white)
                        Text("Загрузка \(Int(progress * 100))%")
                            .font(.caption)
                            .foregroundStyle(.white.opacity(0.7))
                    } else {
                        Text(attachment.formattedSize)
                            .font(.caption)
                            .foregroundStyle(.white.opacity(0.7))
                    }
                }

                Spacer()

                // Иконка: прогресс или скачивание
                if isUploading {
                    ProgressView()
                        .controlSize(.small)
                        .tint(.white)
                } else {
                    Image(systemName: "arrow.down.circle")
                        .font(.title2)
                        .foregroundStyle(.white.opacity(0.7))
                }
            }
            .padding(12)
        }
        .buttonStyle(.plain)
    }
}

#Preview {
    VStack(spacing: 12) {
        DocumentAttachmentView(
            attachment: MessageAttachment(
                id: 1,
                type: .document,
                fileID: "doc1",
                fileName: "Отчёт за 2024 год.pdf",
                fileSize: 3_200_000
            ),
            onTap: {}
        )

        DocumentAttachmentView(
            attachment: MessageAttachment(
                id: 2,
                type: .document,
                fileID: "doc2",
                fileName: "presentation.pptx",
                fileSize: 15_000_000
            ),
            onTap: {}
        )

        DocumentAttachmentView(
            attachment: MessageAttachment(
                id: 3,
                type: .document,
                fileID: "doc3",
                fileName: "archive.zip",
                fileSize: 150_000_000
            ),
            onTap: {}
        )
    }
    .padding()
    .frame(width: 300)
}
