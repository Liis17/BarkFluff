//
//  CloudSettingsView.swift
//  Barkfluff
//
//  Облачное хранилище: общий объём, разбиение по типам файлов.
//

import SwiftUI
import BFCore
import BFNetworking

struct CloudSettingsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = CloudSettingsViewModel()

    var body: some View {
        Form {
            Section("settings.cloud.section.usage") {
                VStack(alignment: .leading, spacing: 12) {
                    HStack(alignment: .firstTextBaseline) {
                        Text(usedSummary)
                            .font(.title3)
                            .fontWeight(.semibold)
                        Spacer()
                        Button {
                            Task { await viewModel.refresh() }
                        } label: {
                            Image(systemName: "arrow.clockwise")
                        }
                        .buttonStyle(.borderless)
                        .disabled(viewModel.isRefreshing)
                    }

                    if let info = viewModel.info, info.limitBytes > 0 {
                        ProgressView(value: viewModel.usedFraction)
                            .tint(viewModel.usedFraction > 0.9 ? .red : .accentColor)
                        Text("settings.cloud.available \(formatBytes(info.availableBytes))")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    } else if viewModel.info != nil {
                        Text("settings.cloud.no_limit")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
                .padding(.vertical, 4)
            }

            Section("settings.cloud.section.by_type") {
                VStack(alignment: .leading, spacing: 12) {
                    CloudStackedBarView(viewModel: viewModel)
                    legend
                }
                .padding(.vertical, 4)
            }

            let nonEmptyTypes = viewModel.displayedTypes.filter { (viewModel.info?.usedByType[$0] ?? 0) > 0 }
            if !nonEmptyTypes.isEmpty {
                Section("settings.cloud.section.by_type_list") {
                    ForEach(nonEmptyTypes, id: \.self) { type in
                        typeRow(type)
                    }
                }
            }

            if let error = viewModel.errorMessage {
                Section {
                    Text(error)
                        .font(.caption)
                        .foregroundStyle(.red)
                }
            }
        }
        .formStyle(.grouped)
        .padding()
        .task {
            viewModel.dependencyContainer = container
            await viewModel.refresh()
        }
    }

    // MARK: - Subviews

    private var legend: some View {
        VStack(alignment: .leading, spacing: 4) {
            let nonZero = viewModel.displayedTypes.filter { (viewModel.info?.usedByType[$0] ?? 0) > 0 }
            ForEach(nonZero, id: \.self) { type in
                let size = viewModel.info?.usedByType[type] ?? 0
                HStack(spacing: 8) {
                    Circle()
                        .fill(type.tintColor)
                        .frame(width: 8, height: 8)
                    Text(type.displayName)
                        .font(.caption)
                    Spacer()
                    Text(formatBytes(size))
                        .font(.caption.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
            }
            if nonZero.isEmpty {
                Text(viewModel.info == nil ? "common.loading" : "settings.cloud.empty")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }

    private func typeRow(_ type: UploadFileType) -> some View {
        let size = viewModel.info?.usedByType[type] ?? 0
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
        }
        .padding(.vertical, 2)
    }

    // MARK: - Helpers

    private var usedSummary: String {
        guard let info = viewModel.info else {
            return String(localized: "common.loading")
        }
        if info.limitBytes > 0 {
            return String(
                localized: "settings.cloud.used_of \(formatBytes(info.usedBytes)) \(formatBytes(info.limitBytes))"
            )
        } else {
            return formatBytes(info.usedBytes)
        }
    }

    private func formatBytes(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}

#Preview {
    CloudSettingsView()
        .environment(DependencyContainer())
}
