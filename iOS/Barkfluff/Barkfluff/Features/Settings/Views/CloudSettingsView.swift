//
//  CloudSettingsView.swift
//  Barkfluff (iOS)
//

import SwiftUI
import BFCore

struct CloudSettingsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = CloudSettingsViewModel()

    var body: some View {
        List {
            if viewModel.isRefreshing && viewModel.info == nil {
                HStack { Spacer(); ProgressView(); Spacer() }
            }

            if let info = viewModel.info {
                Section {
                    LabeledContent("Использовано") {
                        Text(formatBytes(info.usedBytes))
                            .foregroundStyle(.secondary)
                    }
                    if info.limitBytes > 0 {
                        LabeledContent("Лимит") {
                            Text(formatBytes(info.limitBytes))
                                .foregroundStyle(.secondary)
                        }
                        ProgressView(value: viewModel.usedFraction)
                    }
                }

                let nonEmptyTypes = viewModel.displayedTypes.filter { (info.usedByType[$0] ?? 0) > 0 }
                if !nonEmptyTypes.isEmpty {
                    Section("По типам") {
                        ForEach(nonEmptyTypes, id: \.self) { type in
                            let bytes = info.usedByType[type] ?? 0
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
                            }
                        }
                    }
                }
            } else if let err = viewModel.errorMessage {
                Text(err)
                    .foregroundStyle(.red)
                    .font(.footnote)
            }
        }
        .navigationTitle("Облако")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            viewModel.dependencyContainer = container
            await viewModel.refresh()
        }
        .refreshable {
            await viewModel.refresh()
        }
    }

    private func formatBytes(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}
