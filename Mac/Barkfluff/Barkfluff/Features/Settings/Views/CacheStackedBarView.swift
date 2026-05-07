//
//  CacheStackedBarView.swift
//  Barkfluff
//
//  Горизонтальный stacked-bar — показывает доли каждого типа в общем кеше.
//

import SwiftUI
import BFCore

struct CacheStackedBarView: View {
    let stats: CacheStats
    let displayedTypes: [CachedFileType]

    var body: some View {
        GeometryReader { geo in
            HStack(spacing: 0) {
                ForEach(visibleSegments, id: \.type) { segment in
                    Rectangle()
                        .fill(segment.type.tintColor)
                        .frame(width: max(2, geo.size.width * segment.fraction))
                }

                if stats.totalBytes == 0 {
                    Rectangle()
                        .fill(Color.secondary.opacity(0.15))
                }
            }
        }
        .frame(height: 12)
        .clipShape(Capsule())
    }

    private var visibleSegments: [(type: CachedFileType, fraction: Double)] {
        displayedTypes
            .map { (type: $0, fraction: stats.fraction(for: $0)) }
            .filter { $0.fraction > 0 }
    }
}
