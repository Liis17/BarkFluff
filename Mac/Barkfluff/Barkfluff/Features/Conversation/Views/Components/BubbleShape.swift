//
//  BubbleShape.swift
//  Barkfluff
//
//  Форма пузырька сообщения в стиле iMessage с S-образным "хвостиком"
//

import SwiftUI

/// Сторона хвостика пузырька
enum BubbleTailSide {
    case left
    case right
}

/// Единая форма пузырька сообщения с S-образным хвостиком в стиле iMessage
struct MessageBubbleShape: Shape {
    let tailSide: BubbleTailSide
    let showTail: Bool
    let cornerRadius: CGFloat

    init(tailSide: BubbleTailSide, showTail: Bool, cornerRadius: CGFloat = 18) {
        self.tailSide = tailSide
        self.showTail = showTail
        self.cornerRadius = cornerRadius
    }

    func path(in rect: CGRect) -> Path {
        if !showTail {
            return RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                .path(in: rect)
        }

        var path = Path()
        let r = cornerRadius
        let tailWidth: CGFloat = 8
        let tailHeight: CGFloat = 6

        switch tailSide {
        case .right:
            // Исходящее сообщение — хвостик справа снизу
            let smallR: CGFloat = 4

            // Верхний левый угол
            path.move(to: CGPoint(x: rect.minX + r, y: rect.minY))
            // Верхняя линия
            path.addLine(to: CGPoint(x: rect.maxX - r, y: rect.minY))
            // Верхний правый угол
            path.addArc(
                tangent1End: CGPoint(x: rect.maxX, y: rect.minY),
                tangent2End: CGPoint(x: rect.maxX, y: rect.minY + r),
                radius: r
            )
            // Правая линия до начала хвостика
            path.addLine(to: CGPoint(x: rect.maxX, y: rect.maxY - smallR))
            // S-образный хвостик справа: два кубических безье
            path.addCurve(
                to: CGPoint(x: rect.maxX + tailWidth, y: rect.maxY + tailHeight),
                control1: CGPoint(x: rect.maxX, y: rect.maxY + 2),
                control2: CGPoint(x: rect.maxX + tailWidth * 0.5, y: rect.maxY + tailHeight)
            )
            path.addCurve(
                to: CGPoint(x: rect.maxX - smallR, y: rect.maxY),
                control1: CGPoint(x: rect.maxX + tailWidth * 0.3, y: rect.maxY + tailHeight),
                control2: CGPoint(x: rect.maxX, y: rect.maxY)
            )
            // Нижняя линия
            path.addLine(to: CGPoint(x: rect.minX + r, y: rect.maxY))
            // Левый нижний угол
            path.addArc(
                tangent1End: CGPoint(x: rect.minX, y: rect.maxY),
                tangent2End: CGPoint(x: rect.minX, y: rect.maxY - r),
                radius: r
            )
            // Левая линия
            path.addLine(to: CGPoint(x: rect.minX, y: rect.minY + r))
            // Верхний левый угол
            path.addArc(
                tangent1End: CGPoint(x: rect.minX, y: rect.minY),
                tangent2End: CGPoint(x: rect.minX + r, y: rect.minY),
                radius: r
            )

        case .left:
            // Входящее сообщение — хвостик слева снизу
            let smallR: CGFloat = 4

            // Верхний левый угол
            path.move(to: CGPoint(x: rect.minX + r, y: rect.minY))
            // Верхняя линия
            path.addLine(to: CGPoint(x: rect.maxX - r, y: rect.minY))
            // Верхний правый угол
            path.addArc(
                tangent1End: CGPoint(x: rect.maxX, y: rect.minY),
                tangent2End: CGPoint(x: rect.maxX, y: rect.minY + r),
                radius: r
            )
            // Правая линия
            path.addLine(to: CGPoint(x: rect.maxX, y: rect.maxY - r))
            // Правый нижний угол
            path.addArc(
                tangent1End: CGPoint(x: rect.maxX, y: rect.maxY),
                tangent2End: CGPoint(x: rect.maxX - r, y: rect.maxY),
                radius: r
            )
            // Нижняя линия до начала хвостика
            path.addLine(to: CGPoint(x: rect.minX + smallR, y: rect.maxY))
            // S-образный хвостик слева: два кубических безье
            path.addCurve(
                to: CGPoint(x: rect.minX - tailWidth, y: rect.maxY + tailHeight),
                control1: CGPoint(x: rect.minX, y: rect.maxY),
                control2: CGPoint(x: rect.minX - tailWidth * 0.3, y: rect.maxY + tailHeight)
            )
            path.addCurve(
                to: CGPoint(x: rect.minX, y: rect.maxY - smallR),
                control1: CGPoint(x: rect.minX - tailWidth * 0.5, y: rect.maxY + tailHeight),
                control2: CGPoint(x: rect.minX, y: rect.maxY + 2)
            )
            // Левая линия
            path.addLine(to: CGPoint(x: rect.minX, y: rect.minY + r))
            // Верхний левый угол
            path.addArc(
                tangent1End: CGPoint(x: rect.minX, y: rect.minY),
                tangent2End: CGPoint(x: rect.minX + r, y: rect.minY),
                radius: r
            )
        }

        path.closeSubpath()
        return path
    }
}

#Preview("Bubble Shapes") {
    VStack(spacing: 24) {
        // Входящее сообщение с хвостиком (последнее в группе)
        HStack {
            MessageBubbleShape(tailSide: .left, showTail: true, cornerRadius: 18)
                .fill(Color(nsColor: .secondarySystemFill))
                .frame(width: 200, height: 60)
                .padding(.leading, 8)
                .padding(.bottom, 6)
                .overlay {
                    Text("Привет! Как дела?")
                        .padding()
                }
            Spacer()
        }

        // Входящее сообщение без хвостика (не последнее в группе)
        HStack {
            MessageBubbleShape(tailSide: .left, showTail: false, cornerRadius: 18)
                .fill(Color(nsColor: .secondarySystemFill))
                .frame(width: 200, height: 60)
                .overlay {
                    Text("Ещё одно!")
                        .padding()
                }
            Spacer()
        }

        // Исходящее сообщение с хвостиком (последнее в группе)
        HStack {
            Spacer()
            MessageBubbleShape(tailSide: .right, showTail: true, cornerRadius: 18)
                .fill(Color(red: 0, green: 122/255, blue: 1))
                .frame(width: 200, height: 60)
                .padding(.trailing, 8)
                .padding(.bottom, 6)
                .overlay {
                    Text("Всё отлично!")
                        .foregroundStyle(.white)
                        .padding()
                }
        }

        // Исходящее сообщение без хвостика
        HStack {
            Spacer()
            MessageBubbleShape(tailSide: .right, showTail: false, cornerRadius: 18)
                .fill(Color(red: 0, green: 122/255, blue: 1))
                .frame(width: 200, height: 60)
                .overlay {
                    Text("Ещё одно!")
                        .foregroundStyle(.white)
                        .padding()
                }
        }
    }
    .padding()
}

// MARK: - Color Extension for Hex

private extension Color {
    init(hex: String) {
        let hex = hex.trimmingCharacters(in: CharacterSet.alphanumerics.inverted)
        var int: UInt64 = 0
        Scanner(string: hex).scanHexInt64(&int)
        let a, r, g, b: UInt64
        switch hex.count {
        case 3: // RGB (12-bit)
            (a, r, g, b) = (255, (int >> 8) * 17, (int >> 4 & 0xF) * 17, (int & 0xF) * 17)
        case 6: // RGB (24-bit)
            (a, r, g, b) = (255, int >> 16, int >> 8 & 0xFF, int & 0xFF)
        case 8: // ARGB (32-bit)
            (a, r, g, b) = (int >> 24, int >> 16 & 0xFF, int >> 8 & 0xFF, int & 0xFF)
        default:
            (a, r, g, b) = (255, 0, 0, 0)
        }

        self.init(
            .sRGB,
            red: Double(r) / 255,
            green: Double(g) / 255,
            blue: Double(b) / 255,
            opacity: Double(a) / 255
        )
    }
}
