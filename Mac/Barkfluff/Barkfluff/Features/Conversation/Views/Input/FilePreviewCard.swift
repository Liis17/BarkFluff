//
//  FilePreviewCard.swift
//  Barkfluff
//
//  Компактный чип превью документа
//

import SwiftUI

struct FilePreviewCard: View {
    let attachment: SelectedAttachment
    let progress: Double?
    let onRemove: () -> Void

    var body: some View {
        HStack(spacing: 6) {
            // Иконка файла
            Image(systemName: iconForExtension(attachment.fileExtension))
                .font(.title3)
                .foregroundStyle(.secondary)
                .frame(width: 24)

            // Имя и размер
            VStack(alignment: .leading, spacing: 1) {
                Text(attachment.displayName)
                    .font(.caption)
                    .lineLimit(1)
                    .truncationMode(.middle)
                    .frame(maxWidth: 100, alignment: .leading)

                if let progress, progress < 1.0 {
                    ProgressView(value: progress)
                        .tint(.accentColor)
                        .frame(maxWidth: 100)
                } else {
                    Text(attachment.formattedSize)
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
            }

            // Кнопка удаления
            Button(action: onRemove) {
                Image(systemName: "xmark.circle.fill")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
        .background(.fill.tertiary)
        .clipShape(RoundedRectangle(cornerRadius: 8))
    }

    private func iconForExtension(_ ext: String) -> String {
        switch ext.lowercased() {
        case "pdf": return "doc.richtext"
        case "doc", "docx": return "doc.text"
        case "xls", "xlsx": return "tablecells"
        case "ppt", "pptx": return "rectangle.split.3x1"
        case "zip", "rar", "7z", "tar", "gz": return "doc.zipper"
        case "txt", "md", "rtf": return "doc.plaintext"
        case "swift", "py", "js", "ts", "html", "css": return "chevron.left.forwardslash.chevron.right"
        default: return "doc"
        }
    }
}

#Preview {
    VStack(spacing: 8) {
        // Создаем тестовое вложение из данных
        if let testData = "test".data(using: .utf8) {
            let attachment = SelectedAttachment.imageData(data: testData, fileName: "document.pdf")

            FilePreviewCard(
                attachment: attachment,
                progress: nil,
                onRemove: {}
            )

            FilePreviewCard(
                attachment: attachment,
                progress: 0.5,
                onRemove: {}
            )
        }
    }
    .padding()
}
