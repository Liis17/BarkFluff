//
//  SharedDocumentsListView.swift
//  Barkfluff
//
//  Список общих документов
//

import SwiftUI
import BFCore

/// Список документов с иконкой, именем, размером, датой
struct SharedDocumentsListView: View {
    let items: [SharedMediaItem]
    let fileService: FileServiceProtocol
    let isLoading: Bool
    let onLoadMore: () -> Void

    var body: some View {
        LazyVStack(spacing: 4) {
            ForEach(items) { item in
                SharedDocumentRowView(item: item, fileService: fileService)
                    .onAppear {
                        if item.id == items.last?.id {
                            onLoadMore()
                        }
                    }
            }
        }

        if isLoading {
            HStack {
                Spacer()
                ProgressView()
                    .controlSize(.small)
                Spacer()
            }
            .padding(.vertical, Theme.Spacing.sm)
        }
    }
}

/// Строка документа в списке
struct SharedDocumentRowView: View {
    let item: SharedMediaItem
    let fileService: FileServiceProtocol

    @State private var isDownloading = false
    @State private var isHovering = false

    var body: some View {
        Button {
            Task { await downloadAndOpen() }
        } label: {
            HStack(spacing: Theme.Spacing.sm) {
                // Иконка типа файла
                FileIconView(fileName: item.fileName, size: 40)

                VStack(alignment: .leading, spacing: 2) {
                    Text(item.fileName)
                        .font(.callout.weight(.medium))
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .foregroundStyle(.primary)

                    HStack(spacing: 4) {
                        Text(item.formattedSize)
                            .font(.caption)
                            .foregroundStyle(.secondary)

                        Text("•")
                            .font(.caption)
                            .foregroundStyle(.quaternary)

                        Text(item.sentAt, style: .date)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }

                Spacer()

                if isDownloading {
                    ProgressView()
                        .controlSize(.small)
                } else {
                    Image(systemName: "arrow.down.circle")
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.horizontal, Theme.Spacing.sm)
            .padding(.vertical, Theme.Spacing.xs)
            .background {
                RoundedRectangle(cornerRadius: 8)
                    .fill(isHovering ? Color.primary.opacity(0.05) : Color.clear)
            }
        }
        .buttonStyle(.plain)
        .onHover { hovering in isHovering = hovering }
    }

    private func downloadAndOpen() async {
        isDownloading = true
        defer { isDownloading = false }

        do {
            let urlString = try await fileService.getDownloadURL(fileID: item.fileID)
            if let url = URL(string: urlString) {
                // Скачивание
                let (tempURL, _) = try await URLSession.shared.download(from: url)

                // NSSavePanel
                let savePanel = NSSavePanel()
                savePanel.nameFieldStringValue = item.fileName
                savePanel.canCreateDirectories = true

                let response = await savePanel.beginSheetModal(
                    for: NSApp.keyWindow ?? NSWindow()
                )

                if response == .OK, let destination = savePanel.url {
                    try FileManager.default.moveItem(at: tempURL, to: destination)
                    NSWorkspace.shared.activateFileViewerSelecting([destination])
                }
            }
        } catch {
            // TODO: показать ошибку
        }
    }
}

#Preview {
    let container = DependencyContainer()

    SharedDocumentsListView(
        items: [
            SharedMediaItem(
                id: 1,
                messageID: 1,
                type: .document,
                fileID: "test",
                previewURL: nil,
                previewFileID: nil,
                fileName: "Документ.pdf",
                fileSize: 1_024_000,
                sentAt: Date(),
                senderID: 1
            )
        ],
        fileService: container.fileService,
        isLoading: false,
        onLoadMore: {}
    )
    .environment(container)
    .frame(width: 280)
    .padding()
}
