//
//  CacheSettingsView.swift
//  Barkfluff (iOS)
//

import SwiftUI
import BFCore

struct CacheSettingsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = CacheSettingsViewModel()
    @State private var showClearAllConfirm = false

    var body: some View {
        List {
            Section {
                LabeledContent("settings.cache.section.total_inline") {
                    Text(formatBytes(viewModel.stats.totalBytes))
                        .foregroundStyle(.secondary)
                }
            }

            let nonEmptyTypes = viewModel.displayedTypes.filter { (viewModel.stats.bytesByType[$0] ?? 0) > 0 }
            if !nonEmptyTypes.isEmpty {
                Section("settings.cache.section.by_type") {
                    ForEach(nonEmptyTypes, id: \.self) { type in
                        let bytes = viewModel.stats.bytesByType[type] ?? 0
                        HStack {
                            Image(systemName: type.systemImage)
                                .foregroundStyle(type.tintColor)
                                .frame(width: 22)
                            VStack(alignment: .leading) {
                                Text(type.displayName)
                                Text(formatBytes(bytes))
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer()
                            Button {
                                Task { await viewModel.clear(type) }
                            } label: {
                                Image(systemName: "trash")
                            }
                            .buttonStyle(.plain)
                            .foregroundStyle(.secondary)
                            .disabled(viewModel.isClearing)
                        }
                    }
                }
            }

            Section {
                Button(role: .destructive) {
                    showClearAllConfirm = true
                } label: {
                    Text("settings.cache.clear_all")
                }
                .disabled(viewModel.isClearing || viewModel.stats.totalBytes == 0)
            }
        }
        .navigationTitle("settings.category.cache")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            viewModel.dependencyContainer = container
            await viewModel.refreshStats()
        }
        .refreshable {
            await viewModel.refreshStats()
        }
        .confirmationDialog(
            "settings.cache.clear_confirm.title",
            isPresented: $showClearAllConfirm,
            titleVisibility: .visible
        ) {
            Button("settings.cache.clear", role: .destructive) {
                Task { await viewModel.clearAll() }
            }
            Button("common.cancel", role: .cancel) {}
        } message: {
            Text("settings.cache.clear_confirm.message")
        }
    }

    private func formatBytes(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}
