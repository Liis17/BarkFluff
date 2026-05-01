//
//  SessionsView.swift
//  Barkfluff
//
//  Управление активными сессиями
//

import SwiftUI
import BFNetworking

struct SessionsView: View {
    @Bindable var viewModel: SettingsViewModel

    var body: some View {
        Form {
            Section {
                if viewModel.isSessionsLoading {
                    HStack {
                        Spacer()
                        ProgressView()
                            .controlSize(.small)
                        Text("Загрузка сессий...")
                            .foregroundStyle(.secondary)
                        Spacer()
                    }
                    .padding(.vertical, 8)
                } else if viewModel.sessions.isEmpty {
                    Text("Нет активных сессий")
                        .foregroundStyle(.secondary)
                } else {
                    let currentSession = viewModel.sessions.first { $0.deviceId == viewModel.currentDeviceId }
                    let otherSessions = viewModel.sessions.filter { $0.deviceId != viewModel.currentDeviceId }

                    if let current = currentSession {
                        sessionRow(current, isCurrent: true)
                    }

                    ForEach(otherSessions) { session in
                        sessionRow(session, isCurrent: false)
                    }
                }
            } header: {
                Text("Активные сессии")
            } footer: {
                Text("Если вы видите незнакомые устройства, завершите сессию и смените пароль")
            }

            if viewModel.sessions.count > 1 {
                Section {
                    Button("Завершить все кроме текущей", role: .destructive) {
                        Task {
                            await viewModel.terminateAllOtherSessions()
                        }
                    }
                }
            }

            if let error = viewModel.errorMessage {
                Section {
                    Text(error)
                        .foregroundStyle(.red)
                        .font(.caption)
                }
            }
        }
        .formStyle(.grouped)
        .padding()
        .task {
            await viewModel.loadSessions()
        }
    }

    // MARK: - Helpers

    @ViewBuilder
    private func sessionRow(_ session: SessionInfo, isCurrent: Bool) -> some View {
        let localMeta = DeviceMetadataProvider.shared
        let name = !session.displayName.isEmpty
            ? session.displayName
            : (isCurrent ? localMeta.deviceName : "Неизвестное устройство")
        let os = !session.operationSystem.isEmpty
            ? session.operationSystem
            : (isCurrent ? localMeta.osName : "")
        let app = !session.appName.isEmpty
            ? session.appName
            : (isCurrent ? localMeta.appName : "")

        HStack(spacing: 12) {
            Image(systemName: iconForOS(os))
                .font(.title2)
                .foregroundStyle(isCurrent ? Color.accentColor : .secondary)
                .frame(width: 36)

            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 6) {
                    Text(name)
                        .font(.headline)

                    if isCurrent {
                        Text("Текущая")
                            .font(.caption2)
                            .foregroundStyle(.white)
                            .padding(.horizontal, 6)
                            .padding(.vertical, 2)
                            .background(Color.accentColor)
                            .clipShape(Capsule())
                    }
                }

                if !app.isEmpty || !os.isEmpty {
                    HStack(spacing: 4) {
                        if !app.isEmpty {
                            Text(app)
                        }
                        if !os.isEmpty {
                            if !app.isEmpty {
                                Text("·")
                            }
                            Text(os)
                        }
                    }
                    .font(.caption)
                    .foregroundStyle(.secondary)
                }

                HStack(spacing: 4) {
                    if !session.location.isEmpty {
                        Text(session.location)
                        Text("·")
                    }
                    Text(session.createdAt, style: .date)
                }
                .font(.caption)
                .foregroundStyle(.tertiary)
            }

            Spacer()

            if !isCurrent {
                Button("Завершить", role: .destructive) {
                    Task {
                        await viewModel.terminateSession(deviceID: session.deviceId)
                    }
                }
                .buttonStyle(.plain)
                .foregroundStyle(.red)
            }
        }
        .padding(.vertical, 4)
    }

    private func iconForOS(_ os: String) -> String {
        let lower = os.lowercased()
        if lower.contains("mac") || lower.contains("macos") {
            return "desktopcomputer"
        } else if lower.contains("iphone") || lower.contains("ios") {
            return "iphone"
        } else if lower.contains("ipad") {
            return "ipad"
        } else if lower.contains("android") {
            return "smartphone"
        } else if lower.contains("windows") {
            return "pc"
        } else if lower.contains("linux") {
            return "server.rack"
        } else if lower.contains("web") {
            return "globe"
        } else {
            return "questionmark.circle"
        }
    }
}

#Preview {
    SessionsView(viewModel: SettingsViewModel())
}
