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
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Group {
                if isSending {
                    ProgressView()
                        .controlSize(.small)
                        .tint(.white)
                } else {
                    Image(systemName: canSend ? "arrow.up" : "mic")
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
    }
}

#Preview {
    HStack(spacing: 20) {
        SendButton(canSend: false, isSending: false, action: {})
        SendButton(canSend: true, isSending: false, action: {})
        SendButton(canSend: true, isSending: true, action: {})
    }
    .padding()
}
