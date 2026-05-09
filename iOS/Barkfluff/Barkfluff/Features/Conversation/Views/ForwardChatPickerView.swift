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
                "Переслать сообщение",
                systemImage: "arrowshape.turn.up.right.fill",
                description: Text("Экран будет реализован в Разделе 7")
            )
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Отмена") { dismiss() }
                }
            }
        }
    }
}
