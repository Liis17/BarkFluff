//
//  ForwardChatPickerView.swift
//  Barkfluff (iOS)
//
//  Заглушка экрана выбора чата для пересылки. Полная реализация — в Разделе 7.
//

import SwiftUI

struct ForwardChatPickerView: View {
    let messageID: Int64
    let sourceChatID: String

    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            ContentUnavailableView(
                "conversation.forward.title",
                systemImage: "arrowshape.turn.up.right.fill",
                description: Text("conversation.forward.placeholder_description")
            )
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("common.cancel") { dismiss() }
                }
            }
        }
    }
}
