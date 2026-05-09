//
//  SharedMediaGridView.swift
//  Barkfluff (iOS)
//
//  Грид общих медиа-вложений.
//

import SwiftUI
import BFCore

struct SharedMediaGridView: View {
    let items: [SharedMediaItem]

    private let columns = [
        GridItem(.adaptive(minimum: 100), spacing: 4)
    ]

    var body: some View {
        LazyVGrid(columns: columns, spacing: 4) {
            ForEach(items) { item in
                CachedImageView(
                    fileID: item.previewFileID ?? item.fileID,
                    type: .image,
                    content: { image in
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                    },
                    placeholder: {
                        Color(uiColor: .systemGray5)
                    }
                )
                .frame(height: 100)
                .clipped()
                .clipShape(RoundedRectangle(cornerRadius: 6))
            }
        }
    }
}
