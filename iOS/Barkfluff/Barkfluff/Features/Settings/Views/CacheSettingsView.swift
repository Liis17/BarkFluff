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
                LabeledContent("Всего использовано") {
                    Text(formatBytes(viewModel.stats.totalBytes))
                        .foregroundStyle(.secondary)
                }
            }

            Section("По типам") {
                ForEach(viewModel.displayedTypes, id: \.self) { type in
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
                        if bytes > 0 {
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
                    Text("Очистить весь кеш")
                }
                .disabled(viewModel.isClearing || viewModel.stats.totalBytes == 0)
            }
        }
        .navigationTitle("Кеш")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            viewModel.dependencyContainer = container
            await viewModel.refreshStats()
        }
        .refreshable {
            await viewModel.refreshStats()
        }
        .confirmationDialog(
            "Очистить весь кеш?",
            isPresented: $showClearAllConfirm,
            titleVisibility: .visible
        ) {
            Button("Очистить", role: .destructive) {
                Task { await viewModel.clearAll() }
            }
            Button("Отмена", role: .cancel) {}
        } message: {
            Text("Будут удалены все локальные копии медиа, локальная БД сообщений и чатов. Сами сообщения на сервере не пострадают.")
        }
    }

    private func formatBytes(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}
