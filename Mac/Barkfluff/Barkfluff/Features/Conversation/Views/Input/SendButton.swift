//
//  SendButton.swift
//  Barkfluff
//
//  Кнопка отправки с состояниями (inactive / active / sending)
//

import SwiftUI

struct SendButton: View {
    let canSend: Bool
    let isSending: Bool
    /// Режим редактирования — иконка стрелки заменяется галочкой.
    var isEditMode: Bool = false
    let action: () -> Void

    private var iconName: String {
        if isEditMode { return "checkmark" }
        return canSend ? "arrow.up" : "mic"
    }

    var body: some View {
        Button(action: action) {
            Group {
                if isSending {
                    ProgressView()
                        .controlSize(.small)
                        .tint(.white)
                } else {
                    Image(systemName: iconName)
                        .font(.title3)
                        .fontWeight(.semibold)
                        .foregroundStyle(.white)
                }
            }
            .frame(width: 32, height: 32)
            .background(
                Circle()
                    .fill(canSend ? Color.accentColor : Color.secondary.opacity(0.3))
            )
        }
        .buttonStyle(.plain)
        .disabled(!canSend || isSending)
        .animation(.easeInOut(duration: 0.15), value: canSend)
        .animation(.easeInOut(duration: 0.15), value: isSending)
        .animation(.easeInOut(duration: 0.15), value: isEditMode)
    }
}

#Preview {
    HStack(spacing: 20) {
        SendButton(canSend: false, isSending: false, action: {})
        SendButton(canSend: true, isSending: false, action: {})
        SendButton(canSend: true, isSending: true, action: {})
        SendButton(canSend: true, isSending: false, isEditMode: true, action: {})
    }
    .padding()
}
