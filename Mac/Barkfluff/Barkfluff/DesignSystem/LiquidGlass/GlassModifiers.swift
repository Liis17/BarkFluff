//
//  GlassModifiers.swift
//  Barkfluff
//
//  ViewModifiers для эффекта Liquid Glass (macOS 26)
//

import SwiftUI

// MARK: - Glass Effect Modifier

/// Модификатор для добавления эффекта "стеклянной" поверхности
struct GlassEffectModifier: ViewModifier {

    var material: Material = .regularMaterial
    var cornerRadius: CGFloat = 12
    var shadowRadius: CGFloat = 4

    func body(content: Content) -> some View {
        content
            .background(material)
            .clipShape(RoundedRectangle(cornerRadius: cornerRadius))
            .shadow(color: .black.opacity(0.1), radius: shadowRadius, x: 0, y: 2)
    }
}

// MARK: - View Extension

extension View {
    /// Применить эффект "стеклянной" поверхности
    func glassEffect(
        material: Material = .regularMaterial,
        cornerRadius: CGFloat = 12,
        shadowRadius: CGFloat = 4
    ) -> some View {
        modifier(GlassEffectModifier(
            material: material,
            cornerRadius: cornerRadius,
            shadowRadius: shadowRadius
        ))
    }

    /// Эффект "стеклянной" карточки
    func glassCard(padding: CGFloat = 16) -> some View {
        self
            .padding(padding)
            .glassEffect(material: .regularMaterial, cornerRadius: 16)
    }

    /// Эффект "стеклянной" панели
    func glassPanel() -> some View {
        self
            .glassEffect(material: .ultraThinMaterial, cornerRadius: 12)
    }
}

// MARK: - Hover Effect Modifier

/// Модификатор для эффекта при наведении (для macOS)
struct HoverEffectModifier: ViewModifier {
    @State private var isHovered = false
    var scaleAmount: CGFloat = 1.02

    func body(content: Content) -> some View {
        content
            .scaleEffect(isHovered ? scaleAmount : 1.0)
            .animation(.easeInOut(duration: 0.2), value: isHovered)
            .onHover { hovering in
                isHovered = hovering
            }
    }
}

extension View {
    /// Добавить эффект при наведении мыши
    func hoverEffect(scale: CGFloat = 1.02) -> some View {
        modifier(HoverEffectModifier(scaleAmount: scale))
    }
}

// MARK: - Shimmer Effect (для загрузки)

/// Модификатор shimmer-эффекта для загрузки
struct ShimmerModifier: ViewModifier {
    @State private var phase: CGFloat = 0

    func body(content: Content) -> some View {
        content
            .overlay {
                LinearGradient(
                    stops: [
                        .init(color: .clear, location: 0),
                        .init(color: .white.opacity(0.5), location: 0.5),
                        .init(color: .clear, location: 1)
                    ],
                    startPoint: .leading,
                    endPoint: .trailing
                )
                .offset(x: phase)
                .mask(content)
            }
            .onAppear {
                withAnimation(.linear(duration: 1.5).repeatForever(autoreverses: false)) {
                    phase = 400
                }
            }
    }
}

extension View {
    /// Добавить shimmer-эффект для загрузки
    func shimmer() -> some View {
        modifier(ShimmerModifier())
    }
}

// MARK: - Preview

#Preview {
    VStack(spacing: 20) {
        Text("Glass Card")
            .frame(width: 200, height: 100)
            .glassCard()

        
        Text("Glass Panel")
            .frame(width: 200, height: 60)
            .glassPanel()

        
        Text("Hover Me")
            .frame(width: 150, height: 50)
            .glassEffect(material: .thinMaterial)
            .hoverEffect()
    }
    .padding()
    .frame(width: 400, height: 400)
    .background(Color.gray.opacity(0.2))
}
