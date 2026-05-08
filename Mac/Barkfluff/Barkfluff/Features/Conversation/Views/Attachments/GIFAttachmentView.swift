//
//  GIFAttachmentView.swift
//  Barkfluff
//
//  Отображение GIF-вложения (Nuke 12+ поддерживает GIF автоматически)
//

import SwiftUI
import Nuke
import NukeUI
import BFCore

struct GIFAttachmentView: View {
    let attachment: MessageAttachment
    let targetSize: CGSize
    let onTap: () -> Void

    @Environment(DependencyContainer.self) private var container

    var body: some View {
        ZStack {
            // Nuke 12+ автоматически воспроизводит GIF
            CachedImageView(
                fileID: attachment.fileID,
                type: .gif,
                content: { image in
                    image
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                        .frame(width: targetSize.width, height: targetSize.height)
                        .clipped()
                        .clipShape(RoundedRectangle(cornerRadius: CGFloat(container.personalizationSettings.bubbleCornerRadius), style: .continuous))
                },
                placeholder: { loadingPlaceholder }
            )

            // GIF бейдж
            gifBadge
        }
        .frame(width: targetSize.width, height: targetSize.height)
        .contentShape(Rectangle())
        .onTapGesture(perform: onTap)
        .contextMenu {
            Button {
                Task {
                    try? await FileDownloadHelper.downloadToDownloads(
                        fileID: attachment.fileID,
                        fileName: attachment.fileName,
                        fileService: container.fileService,
                        mediaCacheManager: container.mediaCacheManager,
                        cacheType: attachment.type.cacheType
                    )
                }
            } label: {
                Label("Скачать оригинал", systemImage: "arrow.down.circle")
            }
        }
    }

    // MARK: - Private Views

    private var loadingPlaceholder: some View {
        RoundedRectangle(cornerRadius: CGFloat(container.personalizationSettings.bubbleCornerRadius), style: .continuous)
            .fill(.fill.tertiary)
            .overlay {
                ProgressView()
                    .controlSize(.small)
            }
    }

    private var errorPlaceholder: some View {
        RoundedRectangle(cornerRadius: CGFloat(container.personalizationSettings.bubbleCornerRadius), style: .continuous)
            .fill(.fill.tertiary)
            .overlay {
                Image(systemName: "photo")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)
            }
    }

    private var gifBadge: some View {
        Text("GIF")
            .font(.caption2)
            .fontWeight(.semibold)
            .foregroundStyle(.white)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .glassEffect(.clear, in: .capsule)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .padding(6)
    }

}

#Preview {
    GIFAttachmentView(
        attachment: MessageAttachment(
            id: 1,
            type: .gif,
            fileID: "test",
            fileName: "animation.gif",
            fileSize: 2_500_000,
            previewURL: nil
        ),
        targetSize: CGSize(width: 200, height: 200),
        onTap: {}
    )
    .environment(DependencyContainer())
}
