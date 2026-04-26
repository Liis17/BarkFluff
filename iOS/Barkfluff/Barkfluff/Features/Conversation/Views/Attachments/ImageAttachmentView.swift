//
//  ImageAttachmentView.swift
//  Barkfluff
//
//  Отображение изображения во вложении (iOS версия)
//

import SwiftUI
import BFCore
import NukeUI
import Nuke

/// Отображение изображения во вложении
struct ImageAttachmentView: View {
    let attachment: MessageAttachment
    let isOwn: Bool
    let onTap: () -> Void

    @Environment(DependencyContainer.self) private var container
    @State private var imageRequest: ImageRequest?

    var body: some View {
        Group {
            if let request = imageRequest {
                LazyImage(request: request) { state in
                    if let image = state.image {
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                            .frame(maxWidth: 200, maxHeight: 200)
                            .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.lg))
                            .onTapGesture { onTap() }
                    } else {
                        placeholder
                    }
                }
                .pipeline(container.imagePipeline)
            } else {
                placeholder
            }
        }
        .task(id: attachment.fileID) {
            await resolveRequest()
        }
    }

    private func resolveRequest() async {
        // Приоритет: previewURL (готовый URL), fallback: fileID через fileService
        if let previewURL = attachment.previewURL, let url = URL(string: previewURL), url.scheme != nil {
            imageRequest = ImageRequest(url: url, userInfo: [.imageIdKey: attachment.fileID])
            return
        }
        do {
            let urlString = try await container.fileService.getDownloadURL(fileID: attachment.fileID)
            if let url = URL(string: urlString) {
                imageRequest = ImageRequest(url: url, userInfo: [.imageIdKey: attachment.fileID])
            }
        } catch {
            imageRequest = nil
        }
    }

    private var placeholder: some View {
        RoundedRectangle(cornerRadius: Theme.Radius.lg)
            .fill(Color(uiColor: .secondarySystemFill))
            .frame(width: 150, height: 150)
            .overlay {
                Image(systemName: "photo")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)
            }
            .onTapGesture { onTap() }
    }
}

#Preview {
    ImageAttachmentView(
        attachment: MessageAttachment(id: 1, type: .image, fileID: "1", fileName: "photo.jpg", fileSize: 1000, previewURL: nil),
        isOwn: true,
        onTap: {}
    )
    .padding()
}
