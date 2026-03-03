//
//  ImageAttachmentView.swift
//  Barkfluff
//
//  Отображение изображения во вложении (iOS версия)
//

import SwiftUI
import BFCore

/// Отображение изображения во вложении
struct ImageAttachmentView: View {
    let attachment: MessageAttachment
    let isOwn: Bool
    let onTap: () -> Void

    var body: some View {
        Group {
            if let previewURL = attachment.previewURL, let url = URL(string: previewURL) {
                AsyncImage(url: url) { phase in
                    switch phase {
                    case .success(let image):
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                            .frame(maxWidth: 200, maxHeight: 200)
                            .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.lg))
                            .onTapGesture { onTap() }
                    case .failure, .empty:
                        placeholder
                    @unknown default:
                        placeholder
                    }
                }
            } else {
                placeholder
            }
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
