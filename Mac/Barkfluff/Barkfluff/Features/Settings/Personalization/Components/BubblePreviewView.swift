//
//  BubblePreviewView.swift
//  Barkfluff
//
//  Превью отрывка чата для экрана персонализации.
//  Использует MessageBubbleShape напрямую, чтобы видно было, как
//  смотрятся реальные пузыри при выбранном радиусе закругления.
//

import SwiftUI

struct BubblePreviewView: View {
    let cornerRadius: Int

    private let radius: CGFloat
    private static let outgoingColor = Color(red: 0, green: 122/255, blue: 1)

    init(cornerRadius: Int) {
        self.cornerRadius = cornerRadius
        self.radius = CGFloat(cornerRadius)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
            incoming("Привет! Как дела?")
            outgoing("Отлично, спасибо!")
            incoming("Что сегодня делаешь?")
            outgoing("Доделываю клиент BarkFluff на Swift 🍎")
            incoming("Звучит круто")
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(Theme.Spacing.md)
        .background(Color(nsColor: .windowBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.lg, style: .continuous))
    }

    // MARK: - Bubbles

    private func incoming(_ text: String) -> some View {
        HStack {
            Text(text)
                .font(.subheadline)
                .foregroundStyle(.primary)
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.vertical, Theme.Spacing.sm)
                .background(
                    MessageBubbleShape(tailSide: .left, showTail: true, cornerRadius: radius)
                        .fill(Color(nsColor: .secondarySystemFill))
                )
                .clipShape(
                    MessageBubbleShape(tailSide: .left, showTail: true, cornerRadius: radius)
                )
                .padding(.leading, 6)
            Spacer(minLength: 24)
        }
    }

    private func outgoing(_ text: String) -> some View {
        HStack {
            Spacer(minLength: 24)
            Text(text)
                .font(.subheadline)
                .foregroundStyle(.white)
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.vertical, Theme.Spacing.sm)
                .background(
                    MessageBubbleShape(tailSide: .right, showTail: true, cornerRadius: radius)
                        .fill(Self.outgoingColor)
                )
                .clipShape(
                    MessageBubbleShape(tailSide: .right, showTail: true, cornerRadius: radius)
                )
                .padding(.trailing, 6)
        }
    }
}

#Preview("Радиус 18") {
    BubblePreviewView(cornerRadius: 18)
        .padding()
        .frame(width: 380)
}

#Preview("Радиус 0") {
    BubblePreviewView(cornerRadius: 0)
        .padding()
        .frame(width: 380)
}
