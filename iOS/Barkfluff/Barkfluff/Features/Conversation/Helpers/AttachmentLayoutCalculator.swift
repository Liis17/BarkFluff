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
    public static let maxSingleWidth: CGFloat = 520
    public static let maxSingleHeight: CGFloat = 520

    /// Максимальная ширина всей раскладки в широком detail.
    public static let maxLayoutWidth: CGFloat = 520

    /// Размеры для двух вложений
    public static let doubleItemWidth: CGFloat = 128
    public static let doubleItemHeight: CGFloat = 170

    /// Размеры для трёх вложений (1 большой + 2 маленьких)
    public static let tripleMainWidth: CGFloat = 170
    public static let tripleMainHeight: CGFloat = 150
    public static let tripleSmallWidth: CGFloat = 85
    public static let tripleSmallHeight: CGFloat = 73

    /// Размеры для сетки 2x2+
    public static let gridItemSize: CGFloat = 128

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
        let width = min(maxSingleWidth, max(0, containerWidth))
        let height = min(maxSingleHeight, width)

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
        let totalWidth = min(maxLayoutWidth, max(0, containerWidth))
        let itemWidth = max(0, (totalWidth - spacing) / 2)
        let scale = doubleItemWidth > 0 ? itemWidth / doubleItemWidth : 0
        let itemHeight = doubleItemHeight * scale

        let item1 = LayoutItem(
            index: 0,
            frame: CGRect(x: 0, y: 0, width: itemWidth, height: itemHeight),
            size: CGSize(width: itemWidth, height: itemHeight)
        )

        let item2 = LayoutItem(
            index: 1,
            frame: CGRect(x: itemWidth + spacing, y: 0, width: itemWidth, height: itemHeight),
            size: CGSize(width: itemWidth, height: itemHeight)
        )

        return AttachmentLayout(
            items: [item1, item2],
            containerSize: CGSize(width: totalWidth, height: itemHeight),
            hiddenCount: 0
        )
    }

    private static func layoutTriple(containerWidth: CGFloat) -> AttachmentLayout {
        // 1 большой слева, 2 маленьких справа сверху вниз
        let totalWidth = min(maxLayoutWidth, max(0, containerWidth))
        let availableItemWidth = max(0, totalWidth - spacing)
        let compactItemWidth = tripleMainWidth + tripleSmallWidth
        let scale = compactItemWidth > 0 ? availableItemWidth / compactItemWidth : 0
        let mainWidth = tripleMainWidth * scale
        let mainHeight = tripleMainHeight * scale
        let smallWidth = tripleSmallWidth * scale
        let smallHeight = tripleSmallHeight * scale
        let totalHeight = max(mainHeight, smallHeight * 2 + spacing)

        let item1 = LayoutItem(
            index: 0,
            frame: CGRect(x: 0, y: 0, width: mainWidth, height: mainHeight),
            size: CGSize(width: mainWidth, height: mainHeight)
        )

        let item2 = LayoutItem(
            index: 1,
            frame: CGRect(x: mainWidth + spacing, y: 0, width: smallWidth, height: smallHeight),
            size: CGSize(width: smallWidth, height: smallHeight)
        )

        let item3 = LayoutItem(
            index: 2,
            frame: CGRect(x: mainWidth + spacing, y: smallHeight + spacing, width: smallWidth, height: smallHeight),
            size: CGSize(width: smallWidth, height: smallHeight)
        )

        return AttachmentLayout(
            items: [item1, item2, item3],
            containerSize: CGSize(width: totalWidth, height: totalHeight),
            hiddenCount: 0
        )
    }

    private static func layoutGrid(count: Int, containerWidth: CGFloat, hiddenCount: Int) -> AttachmentLayout {
        // Сетка 2x2
        let totalWidth = min(maxLayoutWidth, max(0, containerWidth))
        let itemSize = max(0, (totalWidth - spacing) / 2)
        let totalHeight = itemSize * 2 + spacing

        var items: [LayoutItem] = []

        for i in 0..<count {
            let row = i / 2
            let col = i % 2

            let x = CGFloat(col) * (itemSize + spacing)
            let y = CGFloat(row) * (itemSize + spacing)

            let item = LayoutItem(
                index: i,
                frame: CGRect(x: x, y: y, width: itemSize, height: itemSize),
                size: CGSize(width: itemSize, height: itemSize)
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

// MARK: - LayoutItem Identifiable

extension LayoutItem: Identifiable {
    public var id: Int { index }
}
