//
//  AboutServerSettingsView.swift
//  Barkfluff (iOS)
//

import SwiftUI
import BFCore
import BFNetworking

struct AboutServerSettingsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = AboutServerSettingsViewModel()

    var body: some View {
        Form {
            Section("Сервер навигатора") {
                if let host = viewModel.beaconHost, let port = viewModel.beaconPort {
                    LabeledContent("Beacon", value: "\(host):\(port)")
                } else {
                    Text("Не подключено").foregroundStyle(.secondary)
                }

                HStack {
                    Button {
                        Task { await viewModel.ping() }
                    } label: {
                        if viewModel.isPinging {
                            ProgressView()
                        } else {
                            Text("Проверить пинг")
                        }
                    }
                    .disabled(viewModel.isPinging || viewModel.beaconHost == nil)

                    Spacer()

                    if let ms = viewModel.lastPingMillis {
                        Text("\(ms) мс")
                            .foregroundStyle(.secondary)
                    } else if let err = viewModel.pingError {
                        Text(err)
                            .font(.caption)
                            .foregroundStyle(.red)
                    }
                }
            }

            Section("Микросервисы") {
                if viewModel.services.isEmpty {
                    Text("Нет данных").foregroundStyle(.secondary)
                } else {
                    ForEach(viewModel.services) { entry in
                        HStack {
                            Image(systemName: entry.kind.systemImage)
                                .frame(width: 22)
                                .foregroundStyle(.secondary)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(entry.kind.displayName)
                                Text(entry.address)
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                                    .textSelection(.enabled)
                            }
                            Spacer()
                            if entry.useTLS {
                                Image(systemName: "lock.fill")
                                    .foregroundStyle(.green)
                                    .font(.caption)
                            }
                        }
                    }
                }
            }
        }
        .navigationTitle("О сервере")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            viewModel.dependencyContainer = container
            await viewModel.refresh()
        }
    }
}
