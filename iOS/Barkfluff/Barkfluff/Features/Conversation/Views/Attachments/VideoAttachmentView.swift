//
//  VideoAttachmentView.swift
//  Barkfluff
//
//  Отображение видео во вложении (iOS версия)
//

import SwiftUI
import BFCore

/// Отображение видео во вложении
struct VideoAttachmentView: View {
    let attachment: MessageAttachment
    let isOwn: Bool
    let onTap: () -> Void

    var body: some View {
        ZStack {
            if let previewURL = attachment.previewURL, let url = URL(string: previewURL) {
                AsyncImage(url: url) { phase in
                    switch phase {
                    case .success(let image):
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                    case .failure, .empty:
                        placeholder
                    @unknown default:
                        placeholder
                    }
                }
            } else {
                placeholder
            }

            // Play button overlay
            Circle()
                .fill(.ultraThinMaterial)
                .frame(width: 50, height: 50)
                .overlay {
                    Image(systemName: "play.fill")
                        .font(.title2)
                        .foregroundStyle(.white)
                        .offset(x: 2)
                }
        }
        .frame(width: 200, height: 150)
        .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.lg))
        .onTapGesture { onTap() }
    }

    private var placeholder: some View {
        Rectangle()
            .fill(Color(uiColor: .secondarySystemFill))
            .overlay {
                Image(systemName: "video.fill")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)
            }
    }
}

#Preview {
    VideoAttachmentView(
        attachment: MessageAttachment(id: 1, type: .video, fileID: "1", fileName: "video.mp4", fileSize: 10000, previewURL: nil),
        isOwn: true,
        onTap: {}
    )
    .padding()
}
