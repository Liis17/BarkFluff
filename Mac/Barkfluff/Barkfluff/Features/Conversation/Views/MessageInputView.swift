//
//  MessageInputView.swift
//  Barkfluff
//
//  Поле ввода сообщения в стиле iMessage
//  Парит поверх сообщений с градиентным blur-эффектом
//

import SwiftUI

struct MessageInputView: View {
    @Binding var text: String
    let onSend: () -> Void
    @State private var isAttachMenuOpen = false
    @FocusState private var isTextFieldFocused: Bool

    var body: some View {
        // Поле ввода с градиентным размытием сверху
        HStack(alignment: .bottom, spacing: Theme.Spacing.sm) {
            // Attach button
            Button {
                isAttachMenuOpen = true
            } label: {
                Image(systemName: "plus")
                    .font(.title3)
                    .foregroundStyle(.secondary)
                    .frame(width: 32, height: 32)
            }
            .buttonStyle(.plain)
            .popover(isPresented: $isAttachMenuOpen) {
                VStack(spacing: Theme.Spacing.md) {
                    Button {
                        isAttachMenuOpen = false
                    } label: {
                        Label("Изображение", systemImage: "photo")
                    }

                    Button {
                        isAttachMenuOpen = false
                    } label: {
                        Label("Файл", systemImage: "doc")
                    }
                }
                .padding()
            }

            // Text field - Capsule с Liquid Glass
            TextField("Сообщение...", text: $text, axis: .vertical)
                .textFieldStyle(.plain)
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.vertical, Theme.Spacing.sm)
                .glassEffect(.regular, in: .capsule)
                .focused($isTextFieldFocused)
                .onSubmit {
                    onSend()
                }

            // Send/Mic button
            Button {
                if !text.isEmpty {
                    onSend()
                }
            } label: {
                Image(systemName: text.isEmpty ? "mic" : "arrow.up")
                    .font(.title3)
                    .fontWeight(.semibold)
                    .foregroundStyle(.white)
                    .frame(width: 32, height: 32)
                    .background(
                        Circle()
                            .fill(Color(red: 0, green: 122/255, blue: 1))
                    )
            }
            .buttonStyle(.plain)
            .animation(.easeInOut(duration: 0.15), value: text.isEmpty)
        }
        .padding(.horizontal, Theme.Spacing.md)
        .padding(.vertical, Theme.Spacing.sm)
        .padding(.top, 30)
        .background {
            Rectangle()
                .fill(
                    LinearGradient(
                        gradient: Gradient(stops: [
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0), location: 0),
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0.4), location: 0.4),
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0.7), location: 0.7),
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0.85), location: 1.0)
                        ]),
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )
        }
    }
}

#Preview {
    ZStack(alignment: .bottom) {
        // Имитация сообщений
        ScrollView {
            VStack(spacing: 20) {
                ForEach(0..<15, id: \.self) { i in
                    HStack {
                        if i % 2 == 0 {
                            Text("Сообщение \(i)")
                                .padding()
                                .background(Color.gray.opacity(0.2))
                                .cornerRadius(16)
                            Spacer()
                        } else {
                            Spacer()
                            Text("Сообщение \(i)")
                                .padding()
                                .background(Color.blue)
                                .foregroundColor(.white)
                                .cornerRadius(16)
                        }
                    }
                    .padding(.horizontal)
                }
            }
        }

        // Поле ввода снизу
        MessageInputView(text: .constant("Тест")) {
            print("Send")
        }
    }
    .frame(width: 400, height: 600)
}
