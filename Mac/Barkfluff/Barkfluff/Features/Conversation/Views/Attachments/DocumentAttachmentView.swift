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

    var body: some View {
        Button(action: onTap) {
            HStack(spacing: 12) {
                // Иконка файла
                FileIconView(fileName: attachment.fileName, size: 44)

                // Информация о файле
                VStack(alignment: .leading, spacing: 4) {
                    Text(attachment.fileName)
                        .font(.subheadline)
                        .lineLimit(2)
                        .foregroundStyle(.primary)
                        .multilineTextAlignment(.leading)

                    Text(attachment.formattedSize)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

                Spacer()

                // Иконка скачивания
                Image(systemName: "arrow.down.circle")
                    .font(.title2)
                    .foregroundStyle(.secondary)
            }
            .padding(12)
            .background(.fill.tertiary)
            .clipShape(RoundedRectangle(cornerRadius: 12))
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
