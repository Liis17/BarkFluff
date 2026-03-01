//
//  DragDropOverlay.swift
//  Barkfluff
//
//  Overlay при перетаскивании файлов в область чата
//

import SwiftUI

struct DragDropOverlay: View {
    var body: some View {
        ZStack {
            Rectangle()
                .fill(.ultraThinMaterial)

            VStack(spacing: 12) {
                Image(systemName: "arrow.down.doc")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)

                Text("Перетащите файлы сюда")
                    .font(.title3)
                    .foregroundStyle(.secondary)
            }
            .padding(40)
            .background(
                RoundedRectangle(cornerRadius: 16)
                    .strokeBorder(style: StrokeStyle(lineWidth: 2, dash: [8, 4]))
                    .foregroundStyle(.secondary.opacity(0.5))
            )
        }
    }
}

#Preview {
    ZStack {
        Color.gray.opacity(0.3)
        DragDropOverlay()
    }
}
