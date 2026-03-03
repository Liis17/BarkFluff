//
//  DocumentAttachmentView.swift
//  Barkfluff
//
//  Отображение документа во вложении (iOS версия)
//

import SwiftUI
import BFCore

/// Отображение документа во вложении
struct DocumentAttachmentView: View {
    let attachment: MessageAttachment
    var uploadProgress: Double?
    var onTap: (() -> Void)?

    var body: some View {
        Button {
            onTap?()
        } label: {
            HStack(spacing: Theme.Spacing.sm) {
                // Иконка файла
                FileIconView(fileExtension: attachment.fileExtension, size: 40)

                // Информация о файле
                VStack(alignment: .leading, spacing: 2) {
                    Text(attachment.fileName)
                        .font(.subheadline)
                        .fontWeight(.medium)
                        .foregroundStyle(.primary)
                        .lineLimit(2)

                    Text(attachment.formattedSize)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

                Spacer()

                // Прогресс загрузки
                if let progress = uploadProgress, progress < 1.0 {
                    CircularProgressView(progress: progress)
                        .frame(width: 24, height: 24)
                } else {
                    Image(systemName: "arrow.down.circle")
                        .font(.title2)
                        .foregroundStyle(.secondary)
                }
            }
            .padding(Theme.Spacing.sm)
            .background(Color(uiColor: .tertiarySystemGroupedBackground))
            .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.md))
        }
        .buttonStyle(.plain)
    }
}

// MARK: - CircularProgressView

/// Круговой индикатор прогресса
struct CircularProgressView: View {
    let progress: Double

    var body: some View {
        ZStack {
            Circle()
                .stroke(Color(uiColor: .systemGray4), lineWidth: 2)

            Circle()
                .trim(from: 0, to: progress)
                .stroke(Color.accentColor, style: StrokeStyle(lineWidth: 2, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .animation(.easeInOut(duration: 0.2), value: progress)
        }
    }
}

#Preview {
    VStack(spacing: 16) {
        DocumentAttachmentView(
            attachment: MessageAttachment(
                id: 1,
                type: .document,
                fileID: "1",
                fileName: "Документ.pdf",
                fileSize: 1024000,
                previewURL: nil
            )
        )

        DocumentAttachmentView(
            attachment: MessageAttachment(
                id: 2,
                type: .document,
                fileID: "2",
                fileName: "Очень длинное название файла документа для проверки обрезки текста.docx",
                fileSize: 512000,
                previewURL: nil
            ),
            uploadProgress: 0.6
        )
    }
    .padding()
}
