//
//  CacheSettingsView.swift
//  Barkfluff
//
//  Управление дисковым кешем: общий объём, разбиение по типам, очистка.
//

import SwiftUI
import BFCore

struct CacheSettingsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = CacheSettingsViewModel()

    var body: some View {
        Form {
            Section("settings.cache.section.total") {
                VStack(alignment: .leading, spacing: 12) {
                    HStack {
                        Text(formatBytes(viewModel.stats.totalBytes))
                            .font(.title3)
                            .fontWeight(.semibold)
                        Spacer()
                        Button {
                            Task { await viewModel.refreshStats() }
                        } label: {
                            Image(systemName: "arrow.clockwise")
                        }
                        .buttonStyle(.borderless)
                        .disabled(viewModel.isRefreshing)
                    }

                    ProgressView(value: progressFraction)
                        .tint(.accentColor)
                }
                .padding(.vertical, 4)
            }

            Section("settings.cache.section.by_type") {
                VStack(alignment: .leading, spacing: 12) {
                    CacheStackedBarView(
                        stats: viewModel.stats,
                        displayedTypes: viewModel.displayedTypes
                    )

                    legend
                }
                .padding(.vertical, 4)
            }

            let nonEmptyTypes = viewModel.displayedTypes.filter { (viewModel.stats.bytesByType[$0] ?? 0) > 0 }
            if !nonEmptyTypes.isEmpty {
                Section("settings.cache.section.clear_by_type") {
                    ForEach(nonEmptyTypes, id: \.self) { type in
                        cacheRow(for: type)
                    }
                }
            }

            Section {
                Button(role: .destructive) {
                    Task { await viewModel.clearAll() }
                } label: {
                    HStack {
                        Image(systemName: "trash")
                        Text("settings.cache.clear_all")
                    }
                    .frame(maxWidth: .infinity)
                }
                .disabled(viewModel.isClearing || viewModel.stats.totalBytes == 0)
            }
        }
        .formStyle(.grouped)
        .padding()
        .task {
            viewModel.dependencyContainer = container
            await viewModel.refreshStats()
        }
    }

    // MARK: - Subviews

    private var legend: some View {
        FlowLegend(items: viewModel.displayedTypes.map { type in
            LegendItem(
                color: type.tintColor,
                label: type.displayName,
                size: viewModel.stats.bytesByType[type] ?? 0
            )
        })
    }

    private func cacheRow(for type: CachedFileType) -> some View {
        let size = viewModel.stats.bytesByType[type] ?? 0
        return HStack(spacing: 12) {
            Image(systemName: type.systemImage)
                .foregroundStyle(type.tintColor)
                .frame(width: 24)
            VStack(alignment: .leading, spacing: 2) {
                Text(type.displayName)
                Text(formatBytes(size))
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Button("settings.cache.clear") {
                Task { await viewModel.clear(type) }
            }
            .buttonStyle(.borderless)
            .disabled(viewModel.isClearing || size == 0)
        }
        .padding(.vertical, 2)
    }

    // MARK: - Helpers

    /// Шкала рассчитывается относительно условного «потолка» в 2 ГБ.
    /// Жёстких лимитов нет, поэтому шкала растёт визуально.
    private var progressFraction: Double {
        let cap: Int64 = 2 * 1024 * 1024 * 1024
        return min(1.0, Double(viewModel.stats.totalBytes) / Double(cap))
    }

    private func formatBytes(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}

// MARK: - Legend

private struct LegendItem: Identifiable {
    let color: Color
    let label: String
    let size: Int64
    var id: String { label }
}

private struct FlowLegend: View {
    let items: [LegendItem]

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            ForEach(items.filter { $0.size > 0 }) { item in
                HStack(spacing: 8) {
                    Circle()
                        .fill(item.color)
                        .frame(width: 8, height: 8)
                    Text(item.label)
                        .font(.caption)
                    Spacer()
                    Text(ByteCountFormatter.string(fromByteCount: item.size, countStyle: .file))
                        .font(.caption.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
            }
            if items.allSatisfy({ $0.size == 0 }) {
                Text("settings.cache.empty")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }
}
