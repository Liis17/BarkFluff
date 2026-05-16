//
//  AboutServerSettingsView.swift
//  Barkfluff
//
//  О сервере: общая информация + список микросервисов + пинг через Beacon.
//

import SwiftUI
import BFCore
import BFNetworking

struct AboutServerSettingsView: View {
    @Environment(DependencyContainer.self) private var container
    let serverViewModel: SettingsViewModel
    @State private var viewModel = AboutServerSettingsViewModel()

    var body: some View {
        Form {
            Section("settings.about_server.section.info") {
                if let serverInfo = serverViewModel.serverInfo {
                    LabeledContent("settings.about_server.name", value: serverInfo.name)
                    if let handle = serverInfo.publicHandle {
                        LabeledContent("settings.about_server.address", value: handle)
                    }
                    if let location = serverInfo.location, !location.isEmpty {
                        LabeledContent("settings.about_server.location", value: location)
                    }
                    if let description = serverInfo.description, !description.isEmpty {
                        Text(description)
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }
                } else {
                    Text("settings.about_server.not_connected")
                        .foregroundStyle(.secondary)
                }

                if let host = viewModel.beaconHost, let port = viewModel.beaconPort {
                    LabeledContent("settings.about_server.beacon", value: "\(host):\(port)")
                }
            }

            Section("settings.about_server.section.services") {
                if viewModel.services.isEmpty {
                    Text("settings.about_server.endpoints_empty")
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(viewModel.services) { service in
                        serviceRow(service)
                    }
                }
            }

            Section("settings.about_server.section.connection") {
                HStack(spacing: 12) {
                    Button {
                        Task { await viewModel.ping() }
                    } label: {
                        HStack {
                            if viewModel.isPinging {
                                ProgressView()
                                    .controlSize(.small)
                            } else {
                                Image(systemName: "wave.3.right")
                            }
                            Text(viewModel.isPinging ? "settings.about_server.pinging" : "settings.about_server.ping")
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(viewModel.isPinging || viewModel.beaconHost == nil)

                    Spacer()

                    if let ms = viewModel.lastPingMillis {
                        Label("\(ms) settings.about_server.ms", systemImage: "checkmark.circle.fill")
                            .foregroundStyle(.green)
                    } else if let error = viewModel.pingError {
                        Label(error, systemImage: "exclamationmark.triangle.fill")
                            .foregroundStyle(.red)
                            .lineLimit(2)
                    }
                }
                .padding(.vertical, 2)
            }
        }
        .formStyle(.grouped)
        .padding()
        .task {
            viewModel.dependencyContainer = container
            serverViewModel.dependencyContainer = container
            await serverViewModel.loadServerInfo()
            await viewModel.refresh()
        }
    }

    private func serviceRow(_ service: AboutServerSettingsViewModel.ServiceEntry) -> some View {
        HStack(spacing: 12) {
            Image(systemName: service.kind.systemImage)
                .frame(width: 24)
                .foregroundStyle(.secondary)
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 6) {
                    Text(service.kind.displayName)
                    if service.useTLS {
                        Image(systemName: "lock.fill")
                            .font(.caption2)
                            .foregroundStyle(.green)
                    }
                }
                Text(service.address)
                    .font(.caption.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .textSelection(.enabled)
            }
            Spacer()
        }
        .padding(.vertical, 2)
    }
}

#Preview {
    AboutServerSettingsView(serverViewModel: SettingsViewModel())
        .environment(DependencyContainer())
}
