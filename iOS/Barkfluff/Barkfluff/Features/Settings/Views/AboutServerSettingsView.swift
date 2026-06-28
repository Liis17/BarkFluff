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
            Section("settings.about_server.section.beacon") {
                if let host = viewModel.beaconHost, let port = viewModel.beaconPort {
                    LabeledContent("settings.about_server.beacon", value: "\(host):\(port)")
                } else {
                    Text("settings.about_server.not_connected").foregroundStyle(.secondary)
                }

                HStack {
                    Button {
                        Task { await viewModel.ping() }
                    } label: {
                        if viewModel.isPinging {
                            ProgressView()
                        } else {
                            Text("settings.about_server.ping_check")
                        }
                    }
                    .disabled(viewModel.isPinging || viewModel.beaconHost == nil)

                    Spacer()

                    if let ms = viewModel.lastPingMillis {
                        Text("settings.about_server.ms_short \(ms)")
                            .foregroundStyle(.secondary)
                    } else if let err = viewModel.pingError {
                        Text(err)
                            .font(.caption)
                            .foregroundStyle(.red)
                    }
                }
            }

            Section("settings.about_server.section.services_short") {
                if viewModel.services.isEmpty {
                    Text("settings.about_server.no_data").foregroundStyle(.secondary)
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
        .navigationTitle("settings.category.about_server")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            viewModel.dependencyContainer = container
            await viewModel.refresh()
        }
    }
}
