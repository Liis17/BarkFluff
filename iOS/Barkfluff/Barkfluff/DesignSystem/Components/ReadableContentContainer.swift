//
//  ReadableContentContainer.swift
//  Barkfluff (iOS)
//
//  Ограничивает ширину форм и информационных экранов в широком окне.
//

import SwiftUI

enum ReadableContentWidth {
    static let authentication: CGFloat = 560
    static let form: CGFloat = 720
}

struct ReadableContentContainer<Content: View>: View {
    let maxWidth: CGFloat
    private let content: () -> Content

    init(
        maxWidth: CGFloat,
        @ViewBuilder content: @escaping () -> Content
    ) {
        self.maxWidth = maxWidth
        self.content = content
    }

    var body: some View {
        content()
            .frame(maxWidth: maxWidth)
            .frame(maxWidth: .infinity)
    }
}
