//
//  FlowLayout.swift
//  Barkfluff
//
//  Flow layout для автоматического переноса элементов
//

import SwiftUI

/// Layout для размещения элементов с автоматическим переносом на следующую строку
struct FlowLayout: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let rows = computeRows(proposal: proposal, subviews: subviews)
        let height = rows.reduce(0) { $0 + $1.height + spacing } - spacing
        return CGSize(width: proposal.width ?? 0, height: height > 0 ? height : 0)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let rows = computeRows(proposal: proposal, subviews: subviews)
        var y = bounds.minY

        for row in rows {
            var x = bounds.minX

            for item in row.items {
                let size = item.sizeThatFits(.unspecified)
                item.place(
                    at: CGPoint(x: x, y: y),
                    proposal: ProposedViewSize(size)
                )
                x += size.width + spacing
            }

            y += row.height + spacing
        }
    }

    // MARK: - Private

    private func computeRows(proposal: ProposedViewSize, subviews: Subviews) -> [Row] {
        var rows: [Row] = []
        var currentRowItems: [LayoutSubview] = []
        var currentRowWidth: CGFloat = 0
        let availableWidth = proposal.width ?? 0

        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)

            if currentRowWidth + size.width + spacing > availableWidth && !currentRowItems.isEmpty {
                // Перенос на новую строку
                let rowHeight = currentRowItems.map { $0.sizeThatFits(.unspecified).height }.max() ?? 0
                rows.append(Row(items: currentRowItems, height: rowHeight))

                currentRowItems = [subview]
                currentRowWidth = size.width
            } else {
                currentRowItems.append(subview)
                currentRowWidth += size.width + spacing
            }
        }

        // Добавляем последнюю строку
        if !currentRowItems.isEmpty {
            let rowHeight = currentRowItems.map { $0.sizeThatFits(.unspecified).height }.max() ?? 0
            rows.append(Row(items: currentRowItems, height: rowHeight))
        }

        return rows
    }

    private struct Row {
        let items: [LayoutSubview]
        let height: CGFloat
    }
}

// MARK: - View Extension

extension View {
    /// Применить flow layout
    func flowLayout(spacing: CGFloat = 8) -> some View {
        self.layoutPriority(0)
    }
}

// MARK: - Preview

#Preview {
    VStack {
        FlowLayout(spacing: 8) {
            ForEach(["SwiftUI", "UIKit", "Combine", "Async/Await", "Core Data", "CloudKit", "Widget", "SwiftData"], id: \.self) { tag in
                Text(tag)
                    .font(.caption)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 6)
                    .background(Color.accentColor.opacity(0.1))
                    .clipShape(Capsule())
            }
        }
        .padding()
    }
    .frame(width: 300)
}
