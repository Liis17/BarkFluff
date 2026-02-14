//
//  GlassButtonStyle.swift
//  Barkfluff
//
//  ButtonStyle в стиле Liquid Glass
//

import SwiftUI

// MARK: - Glass Button Style

/// Стиль кнопки с эффектом стекла
struct GlassButtonStyle: ButtonStyle {
    var prominence: Prominence = .standard

    enum Prominence {
        case standard
        case prominent
        case destructive
    }

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(.medium))
            .foregroundStyle(foregroundColor)
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background(background(for: configuration.isPressed))
            .clipShape(RoundedRectangle(cornerRadius: 10))
            .scaleEffect(configuration.isPressed ? 0.98 : 1.0)
            .animation(.easeInOut(duration: 0.15), value: configuration.isPressed)
    }

    @ViewBuilder
    private func background(for isPressed: Bool) -> some View {
        switch prominence {
        case .standard:
            if isPressed {
                Color.primary.opacity(0.15)
            } else {
                Color.primary.opacity(0.08)
            }
        case .prominent:
            if isPressed {
                Color.accentColor.opacity(0.8)
            } else {
                Color.accentColor
            }
        case .destructive:
            if isPressed {
                Color.red.opacity(0.8)
            } else {
                Color.red
            }
        }
    }

    private var foregroundColor: Color {
        switch prominence {
        case .standard:
            return .primary
        case .prominent, .destructive:
            return .white
        }
    }
}

// MARK: - Glass Capsule Button Style

/// Стиль кнопки в форме капсулы
struct GlassCapsuleButtonStyle: ButtonStyle {
    var prominence: GlassButtonStyle.Prominence = .standard

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(.medium))
            .foregroundStyle(foregroundColor)
            .padding(.horizontal, 20)
            .padding(.vertical, 12)
            .background(background(for: configuration.isPressed))
            .clipShape(Capsule())
            .scaleEffect(configuration.isPressed ? 0.96 : 1.0)
            .animation(.easeInOut(duration: 0.15), value: configuration.isPressed)
    }

    @ViewBuilder
    private func background(for isPressed: Bool) -> some View {
        switch prominence {
        case .standard:
            if isPressed {
                Color.primary.opacity(0.15)
            } else {
                Color.primary.opacity(0.08)
            }
        case .prominent:
            if isPressed {
                Color.accentColor.opacity(0.8)
            } else {
                Color.accentColor
            }
        case .destructive:
            if isPressed {
                Color.red.opacity(0.8)
            } else {
                Color.red
            }
        }
    }

    private var foregroundColor: Color {
        switch prominence {
        case .standard:
            return .primary
        case .prominent, .destructive:
            return .white
        }
    }
}

// MARK: - View Extension

extension ButtonStyle where Self == GlassButtonStyle {
    static var bfGlass: GlassButtonStyle { GlassButtonStyle() }
    static var bfGlassProminent: GlassButtonStyle { GlassButtonStyle(prominence: .prominent) }
    static var bfGlassDestructive: GlassButtonStyle { GlassButtonStyle(prominence: .destructive) }
}

extension ButtonStyle where Self == GlassCapsuleButtonStyle {
    static var bfGlassCapsule: GlassCapsuleButtonStyle { GlassCapsuleButtonStyle() }
    static var bfGlassCapsuleProminent: GlassCapsuleButtonStyle { GlassCapsuleButtonStyle(prominence: .prominent) }
}

// MARK: - Preview

#Preview {
    VStack(spacing: 20) {
        HStack(spacing: 12) {
            Button("Standard") {}
                .buttonStyle(.bfGlass)

            Button("Prominent") {}
                .buttonStyle(.bfGlassProminent)

            Button("Destructive") {}
                .buttonStyle(.bfGlassDestructive)
        }

        HStack(spacing: 12) {
            Button("Capsule") {}
                .buttonStyle(.bfGlassCapsule)

            Button("Capsule Prominent") {}
                .buttonStyle(.bfGlassCapsuleProminent)
        }
    }
    .padding()
    .frame(width: 500, height: 200)
}
