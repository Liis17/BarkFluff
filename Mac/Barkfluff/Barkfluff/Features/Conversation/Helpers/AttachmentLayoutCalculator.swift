//
//  AttachmentLayoutCalculator.swift
//  Barkfluff
//
//  Калькулятор раскладки вложений в сообщении
//  Правила раскладки в стиле Telegram/WhatsApp
//

import SwiftUI
import BFCore

/// Результат расчёта раскладки вложений
public struct AttachmentLayout {
    /// Размеры и позиции для каждого вложения
    public let items: [LayoutItem]

    /// Общий размер контейнера
    public let containerSize: CGSize

    /// Количество скрытых вложений (для "+N" бейджа)
    public let hiddenCount: Int
}

public struct LayoutItem {
    public let index: Int
    public let frame: CGRect
    public let size: CGSize
}

/// Калькулятор раскладки вложений
public enum AttachmentLayoutCalculator {

    // MARK: - Constants

    /// Максимальная ширина для одного вложения
    public static let maxSingleWidth: CGFloat = 300
    public static let maxSingleHeight: CGFloat = 300

    /// Размеры для двух вложений
    public static let doubleItemWidth: CGFloat = 148
    public static let doubleItemHeight: CGFloat = 200

    /// Размеры для трёх вложений (1 большой + 2 маленьких)
    public static let tripleMainWidth: CGFloat = 200
    public static let tripleMainHeight: CGFloat = 180
    public static let tripleSmallWidth: CGFloat = 95
    public static let tripleSmallHeight: CGFloat = 85

    /// Размеры для сетки 2x2+
    public static let gridItemSize: CGFloat = 146

    /// Отступ между вложениями
    public static let spacing: CGFloat = 4

    /// Максимальное отображаемое количество (остальные в "+N")
    public static let maxVisibleCount = 4

    // MARK: - Public Methods

    /// Рассчитать раскладку для массива вложений
    public static func calculateLayout(for attachments: [MessageAttachment], containerWidth: CGFloat) -> AttachmentLayout {
        let count = min(attachments.count, maxVisibleCount)
        let hiddenCount = max(0, attachments.count - maxVisibleCount)

        switch count {
        case 0:
            return AttachmentLayout(items: [], containerSize: .zero, hiddenCount: 0)

        case 1:
            return layoutSingle(containerWidth: containerWidth)

        case 2:
            return layoutDouble(containerWidth: containerWidth)

        case 3:
            return layoutTriple(containerWidth: containerWidth)

        default: // 4+
            return layoutGrid(count: count, containerWidth: containerWidth, hiddenCount: hiddenCount)
        }
    }

    // MARK: - Layout Variants

    private static func layoutSingle(containerWidth: CGFloat) -> AttachmentLayout {
        let width = min(maxSingleWidth, containerWidth)
        let height = maxSingleHeight

        let item = LayoutItem(
            index: 0,
            frame: CGRect(x: 0, y: 0, width: width, height: height),
            size: CGSize(width: width, height: height)
        )

        return AttachmentLayout(
            items: [item],
            containerSize: CGSize(width: width, height: height),
            hiddenCount: 0
        )
    }

    private static func layoutDouble(containerWidth: CGFloat) -> AttachmentLayout {
        let totalWidth = doubleItemWidth * 2 + spacing

        let item1 = LayoutItem(
            index: 0,
            frame: CGRect(x: 0, y: 0, width: doubleItemWidth, height: doubleItemHeight),
            size: CGSize(width: doubleItemWidth, height: doubleItemHeight)
        )

        let item2 = LayoutItem(
            index: 1,
            frame: CGRect(x: doubleItemWidth + spacing, y: 0, width: doubleItemWidth, height: doubleItemHeight),
            size: CGSize(width: doubleItemWidth, height: doubleItemHeight)
        )

        return AttachmentLayout(
            items: [item1, item2],
            containerSize: CGSize(width: totalWidth, height: doubleItemHeight),
            hiddenCount: 0
        )
    }

    private static func layoutTriple(containerWidth: CGFloat) -> AttachmentLayout {
        // 1 большой слева, 2 маленьких справа сверху вниз
        let totalWidth = tripleMainWidth + spacing + tripleSmallWidth
        let totalHeight = tripleMainHeight

        let item1 = LayoutItem(
            index: 0,
            frame: CGRect(x: 0, y: 0, width: tripleMainWidth, height: tripleMainHeight),
            size: CGSize(width: tripleMainWidth, height: tripleMainHeight)
        )

        let item2 = LayoutItem(
            index: 1,
            frame: CGRect(x: tripleMainWidth + spacing, y: 0, width: tripleSmallWidth, height: tripleSmallHeight),
            size: CGSize(width: tripleSmallWidth, height: tripleSmallHeight)
        )

        let item3 = LayoutItem(
            index: 2,
            frame: CGRect(x: tripleMainWidth + spacing, y: tripleSmallHeight + spacing, width: tripleSmallWidth, height: tripleSmallHeight),
            size: CGSize(width: tripleSmallWidth, height: tripleSmallHeight)
        )

        return AttachmentLayout(
            items: [item1, item2, item3],
            containerSize: CGSize(width: totalWidth, height: totalHeight),
            hiddenCount: 0
        )
    }

    private static func layoutGrid(count: Int, containerWidth: CGFloat, hiddenCount: Int) -> AttachmentLayout {
        // Сетка 2x2
        let totalWidth = gridItemSize * 2 + spacing
        let totalHeight = gridItemSize * 2 + spacing

        var items: [LayoutItem] = []

        for i in 0..<count {
            let row = i / 2
            let col = i % 2

            let x = CGFloat(col) * (gridItemSize + spacing)
            let y = CGFloat(row) * (gridItemSize + spacing)

            let item = LayoutItem(
                index: i,
                frame: CGRect(x: x, y: y, width: gridItemSize, height: gridItemSize),
                size: CGSize(width: gridItemSize, height: gridItemSize)
            )
            items.append(item)
        }

        return AttachmentLayout(
            items: items,
            containerSize: CGSize(width: totalWidth, height: totalHeight),
            hiddenCount: hiddenCount
        )
    }
}
