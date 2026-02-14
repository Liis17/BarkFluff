//
//  AttachmentPickerView.swift
//  Barkfluff
//
//  Выбор вложений для сообщения
//

import SwiftUI
import UniformTypeIdentifiers

struct AttachmentPickerView: View {
    @Binding var selectedFiles: [URL]
    @State private var isFileImporterPresented = false

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            if !selectedFiles.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(selectedFiles, id: \.absoluteString) { url in
                            AttachmentPreviewChip(url: url) {
                                selectedFiles.removeAll { $0 == url }
                            }
                        }
                    }
                    .padding(.horizontal, 4)
                }
                .frame(height: 40)
            }

            HStack(spacing: 12) {
                Button {
                    isFileImporterPresented = true
                } label: {
                    Label("Файл", systemImage: "paperclip")
                }
                .buttonStyle(.borderless)
            }
        }
        .fileImporter(
            isPresented: $isFileImporterPresented,
            allowedContentTypes: [.image, .movie, .pdf, .data],
            allowsMultipleSelection: true
        ) { result in
            if case .success(let urls) = result {
                selectedFiles.append(contentsOf: urls)
            }
        }
    }
}

private struct AttachmentPreviewChip: View {
    let url: URL
    let onRemove: () -> Void

    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: iconForFile(url))
                .font(.caption)

            Text(url.lastPathComponent)
                .font(.caption)
                .lineLimit(1)

            Button(action: onRemove) {
                Image(systemName: "xmark.circle.fill")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.borderless)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .background(.fill.tertiary)
        .clipShape(Capsule())
    }

    private func iconForFile(_ url: URL) -> String {
        let ext = url.pathExtension.lowercased()
        switch ext {
        case "png", "jpg", "jpeg", "heic", "webp", "gif":
            return "photo"
        case "mp4", "mov", "avi":
            return "video"
        case "pdf":
            return "doc.richtext"
        default:
            return "doc"
        }
    }
}

#Preview {
    AttachmentPickerView(selectedFiles: .constant([]))
        .frame(width: 400)
}
