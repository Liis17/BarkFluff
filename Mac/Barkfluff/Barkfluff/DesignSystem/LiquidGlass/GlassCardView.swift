//
//  GlassCardView.swift
//  Barkfluff
//
//  Контейнер-карточка с эффектом Liquid Glass
//

import SwiftUI

/// Карточка с эффектом Liquid Glass
struct GlassCardView<Content: View>: View {
    var content: Content

    var cornerRadius: CGFloat = 16
    var padding: CGFloat = 16
    var material: Material = .regularMaterial
    var shadowRadius: CGFloat = 4

    init(
        cornerRadius: CGFloat = 16,
        padding: CGFloat = 16,
        material: Material = .regularMaterial,
        shadowRadius: CGFloat = 4,
        @ViewBuilder content: () -> Content
    ) {
        self.cornerRadius = cornerRadius
        self.padding = padding
        self.material = material
        self.shadowRadius = shadowRadius
        self.content = content()
    }

    var body: some View {
        content
            .padding(padding)
            .background(material)
            .clipShape(RoundedRectangle(cornerRadius: cornerRadius))
            .shadow(color: .black.opacity(0.1), radius: shadowRadius, x: 0, y: 2)
    }
}

// MARK: - Interactive Glass Card

/// Интерактивная карточка с hover-эффектом
struct InteractiveGlassCardView<Content: View>: View {
    var content: Content
    var action: () -> Void

    @State private var isHovered = false
    @State private var isPressed = false

    var cornerRadius: CGFloat = 16
    var padding: CGFloat = 16

    init(
        cornerRadius: CGFloat = 16,
        padding: CGFloat = 16,
        action: @escaping () -> Void,
        @ViewBuilder content: () -> Content
    ) {
        self.cornerRadius = cornerRadius
        self.padding = padding
        self.action = action
        self.content = content()
    }

    var body: some View {
        Button(action: action) {
            content
                .padding(padding)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background {
                    RoundedRectangle(cornerRadius: cornerRadius)
                        .fill(backgroundMaterial)
                }
                .overlay {
                    RoundedRectangle(cornerRadius: cornerRadius)
                        .strokeBorder(.white.opacity(isHovered ? 0.2 : 0.1), lineWidth: 1)
                }
                .shadow(color: .black.opacity(0.1), radius: isHovered ? 8 : 4, y: isHovered ? 4 : 2)
                .scaleEffect(isPressed ? 0.98 : 1.0)
                .animation(.easeInOut(duration: 0.2), value: isHovered)
                .animation(.easeInOut(duration: 0.1), value: isPressed)
        }
        .buttonStyle(.plain)
        .onHover { hovering in
            isHovered = hovering
        }
        .simultaneousGesture(
            DragGesture(minimumDistance: 0)
                .onChanged { _ in isPressed = true }
                .onEnded { _ in isPressed = false }
        )
    }

    private var backgroundMaterial: Material {
        if isHovered {
            return .thickMaterial
        } else {
            return .regularMaterial
        }
    }
}

// MARK: - Glass Section Header

/// Заголовок секции в стиле Glass
struct GlassSectionHeader: View {
    let title: String
    var icon: String? = nil

    var body: some View {
        HStack(spacing: 8) {
            if let icon {
                Image(systemName: icon)
                    .foregroundStyle(.secondary)
            }

            Text(title)
                .font(.headline)
                .foregroundStyle(.secondary)

            Spacer()
        }
        .padding(.horizontal, 4)
    }
}

// MARK: - Preview

#Preview {
    VStack(spacing: 20) {
        GlassCardView {
            VStack(alignment: .leading, spacing: 8) {
                Text("Glass Card")
                    .font(.headline)
                Text("Контент карточки с эффектом стекла")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
        }

        InteractiveGlassCardView {
            print("Tapped")
        } content: {
            VStack(alignment: .leading, spacing: 8) {
                Text("Interactive Card")
                    .font(.headline)
                Text("Наведите курсор и кликните")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
        }

        GlassSectionHeader(title: "Секция", icon: "folder")
    }
    .padding()
    .frame(width: 400, height: 350)
    .background(Color.gray.opacity(0.2))
}
