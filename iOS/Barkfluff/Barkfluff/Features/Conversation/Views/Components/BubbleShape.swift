//
//  BubbleShape.swift
//  Barkfluff
//
//  Форма пузырька сообщения в стиле iMessage с S-образным "хвостиком" (iOS версия)
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
        let w = rect.width
        let h = rect.height

        if !showTail {
            return RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                .path(in: rect)
        }

        let r = cornerRadius
        var path = Path()

        switch tailSide {
        case .right:
            // Исходящее — тело на полную ширину, хвостик выходит за rect вправо
            path.move(to: CGPoint(x: w - 15, y: h))
            // Нижняя линия влево
            path.addLine(to: CGPoint(x: r, y: h))
            // Левый нижний угол
            path.addArc(
                tangent1End: CGPoint(x: 0, y: h),
                tangent2End: CGPoint(x: 0, y: h - r),
                radius: r
            )
            // Левая линия вверх
            path.addLine(to: CGPoint(x: 0, y: r))
            // Верхний левый угол
            path.addArc(
                tangent1End: CGPoint(x: 0, y: 0),
                tangent2End: CGPoint(x: r, y: 0),
                radius: r
            )
            // Верхняя линия вправо
            path.addLine(to: CGPoint(x: w - r, y: 0))
            // Верхний правый угол (симметричный, на полную ширину)
            path.addArc(
                tangent1End: CGPoint(x: w, y: 0),
                tangent2End: CGPoint(x: w, y: r),
                radius: r
            )
            // Правый край вниз до начала хвостика
            path.addLine(to: CGPoint(x: w, y: h - 12))
            // Хвостик: плавная кривая к кончику (выходит за rect)
            path.addCurve(
                to: CGPoint(x: w + 5, y: h),
                control1: CGPoint(x: w, y: h - 1),
                control2: CGPoint(x: w + 5, y: h)
            )
            // Кончик хвостика
            path.addLine(to: CGPoint(x: w + 6, y: h))
            // Обратная S-кривая назад
            path.addCurve(
                to: CGPoint(x: w - 7, y: h - 4),
                control1: CGPoint(x: w + 1, y: h + 1),
                control2: CGPoint(x: w - 3, y: h - 1)
            )
            // Соединение с нижним краем
            path.addCurve(
                to: CGPoint(x: w - 15, y: h),
                control1: CGPoint(x: w - 11, y: h),
                control2: CGPoint(x: w - 15, y: h)
            )

        case .left:
            // Входящее — тело на полную ширину, хвостик выходит за rect влево
            path.move(to: CGPoint(x: 15, y: h))
            // Нижняя линия вправо
            path.addLine(to: CGPoint(x: w - r, y: h))
            // Правый нижний угол
            path.addArc(
                tangent1End: CGPoint(x: w, y: h),
                tangent2End: CGPoint(x: w, y: h - r),
                radius: r
            )
            // Правая линия вверх
            path.addLine(to: CGPoint(x: w, y: r))
            // Верхний правый угол
            path.addArc(
                tangent1End: CGPoint(x: w, y: 0),
                tangent2End: CGPoint(x: w - r, y: 0),
                radius: r
            )
            // Верхняя линия влево
            path.addLine(to: CGPoint(x: r, y: 0))
            // Верхний левый угол (симметричный, на полную ширину)
            path.addArc(
                tangent1End: CGPoint(x: 0, y: 0),
                tangent2End: CGPoint(x: 0, y: r),
                radius: r
            )
            // Левый край вниз до начала хвостика
            path.addLine(to: CGPoint(x: 0, y: h - 12))
            // Хвостик: плавная кривая к кончику (выходит за rect)
            path.addCurve(
                to: CGPoint(x: -5, y: h),
                control1: CGPoint(x: 0, y: h - 1),
                control2: CGPoint(x: -5, y: h)
            )
            // Кончик хвостика
            path.addLine(to: CGPoint(x: -6, y: h))
            // Обратная S-кривая назад
            path.addCurve(
                to: CGPoint(x: 7, y: h - 4),
                control1: CGPoint(x: -1, y: h + 1),
                control2: CGPoint(x: 3, y: h - 1)
            )
            // Соединение с нижним краем
            path.addCurve(
                to: CGPoint(x: 15, y: h),
                control1: CGPoint(x: 11, y: h),
                control2: CGPoint(x: 15, y: h)
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
            Text("Привет! Как дела?")
                .padding()
                .background(
                    MessageBubbleShape(tailSide: .left, showTail: true)
                        .fill(Color(uiColor: .tertiarySystemFill))
                )
            Spacer()
        }

        // Входящее сообщение без хвостика (не последнее в группе)
        HStack {
            Text("Ещё одно!")
                .padding()
                .background(
                    MessageBubbleShape(tailSide: .left, showTail: false)
                        .fill(Color(uiColor: .tertiarySystemFill))
                )
            Spacer()
        }

        // Исходящее сообщение с хвостиком (последнее в группе)
        HStack {
            Spacer()
            Text("Всё отлично!")
                .foregroundStyle(.white)
                .padding()
                .background(
                    MessageBubbleShape(tailSide: .right, showTail: true)
                        .fill(Color(red: 0, green: 122/255, blue: 1))
                )
        }

        // Исходящее сообщение без хвостика
        HStack {
            Spacer()
            Text("Ещё одно!")
                .foregroundStyle(.white)
                .padding()
                .background(
                    MessageBubbleShape(tailSide: .right, showTail: false)
                        .fill(Color(red: 0, green: 122/255, blue: 1))
                )
        }
    }
    .padding()
}
