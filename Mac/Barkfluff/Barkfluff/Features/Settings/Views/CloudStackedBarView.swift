//
//  CloudStackedBarView.swift
//  Barkfluff
//
//  Горизонтальный stacked-bar — доли типов в общем объёме облачного хранилища.
//

import SwiftUI
import BFCore
import BFNetworking

struct CloudStackedBarView: View {
    let viewModel: CloudSettingsViewModel

    var body: some View {
        GeometryReader { geo in
            HStack(spacing: 0) {
                ForEach(visibleSegments, id: \.type) { segment in
                    Rectangle()
                        .fill(segment.type.tintColor)
                        .frame(width: max(2, geo.size.width * segment.fraction))
                }

                if (viewModel.info?.usedBytes ?? 0) == 0 {
                    Rectangle()
                        .fill(Color.secondary.opacity(0.15))
                }
            }
        }
        .frame(height: 12)
        .clipShape(Capsule())
    }

    private var visibleSegments: [(type: UploadFileType, fraction: Double)] {
        viewModel.displayedTypes
            .map { (type: $0, fraction: viewModel.fraction(for: $0)) }
            .filter { $0.fraction > 0 }
    }
}
