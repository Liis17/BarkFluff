//
//  SessionsView.swift
//  Barkfluff
//
//  Управление активными сессиями
//

import SwiftUI

struct SessionsView: View {
    @Bindable var viewModel: SettingsViewModel

    var body: some View {
        Form {
            Section {
                if viewModel.sessions.isEmpty {
                    Text("Нет активных сессий")
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(viewModel.sessions) { session in
                        HStack {
                            Image(systemName: session.deviceType.icon)
                                .font(.title2)
                                .foregroundStyle(Color.accentColor)
                                .frame(width: 40)

                            VStack(alignment: .leading, spacing: 2) {
                                Text(session.deviceName)
                                    .font(.headline)
                                Text(session.lastActive, style: .relative)
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }

                            Spacer()

                            if !session.isCurrent {
                                Button("Завершить", role: .destructive) {
                                    Task {
                                        await viewModel.terminateSession(session.id)
                                    }
                                }
                                .buttonStyle(.plain)
                                .foregroundStyle(.red)
                            } else {
                                Text("Текущая")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                                    .padding(.horizontal, 8)
                                    .padding(.vertical, 4)
                                    .background(Color.accentColor.opacity(0.2))
                                    .clipShape(Capsule())
                            }
                        }
                        .padding(.vertical, 4)
                    }
                }
            } header: {
                Text("Активные сессии")
            } footer: {
                Text("Если вы видите незнакомые устройства, завершите сессию и смените пароль")
            }
        }
        .formStyle(.grouped)
        .padding()
    }
}

// Preview data models
struct Session: Identifiable {
    let id: String
    let deviceName: String
    let deviceType: DeviceType
    let lastActive: Date
    let isCurrent: Bool

    enum DeviceType {
        case mac, iphone, ipad, web, other

        var icon: String {
            switch self {
            case .mac: return "desktopcomputer"
            case .iphone: return "iphone"
            case .ipad: return "ipad"
            case .web: return "globe"
            case .other: return "questionmark.circle"
            }
        }
    }
}

#Preview {
    SessionsView(viewModel: SettingsViewModel())
}
