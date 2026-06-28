//
//  SessionsView.swift
//  Barkfluff (iOS)
//

import SwiftUI
import BFCore
import BFNetworking

struct SessionsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = SettingsViewModel()
    @State private var showTerminateAllConfirm = false
    @State private var showFastAuthScanner = false

    var body: some View {
        List {
            if viewModel.isSessionsLoading && viewModel.sessions.isEmpty {
                HStack {
                    Spacer()
                    ProgressView()
                    Spacer()
                }
            }

            if let err = viewModel.errorMessage {
                Section {
                    Text(err).foregroundStyle(.red).font(.footnote)
                }
            }

            Section {
                Button {
                    showFastAuthScanner = true
                } label: {
                    HStack(spacing: 12) {
                        Image(systemName: "qrcode.viewfinder")
                            .font(.system(size: 16, weight: .semibold))
                            .foregroundStyle(.white)
                            .frame(width: 30, height: 30)
                            .background(Color.blue.gradient, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                        Text("settings.sessions.connect_qr")
                            .foregroundStyle(.primary)
                        Spacer()
                        Image(systemName: "chevron.right")
                            .font(.footnote.weight(.semibold))
                            .foregroundStyle(.tertiary)
                    }
                }
                .buttonStyle(.plain)
            }

            Section {
                ForEach(viewModel.sessions, id: \.deviceId) { session in
                    SessionRow(
                        session: session,
                        isCurrent: session.deviceId.caseInsensitiveCompare(viewModel.currentDeviceId) == .orderedSame,
                        onTerminate: {
                            Task { await viewModel.terminateSession(deviceID: session.deviceId) }
                        }
                    )
                }
            }

            if viewModel.sessions.count > 1 {
                Section {
                    Button(role: .destructive) {
                        showTerminateAllConfirm = true
                    } label: {
                        Text("settings.sessions.terminate_all_others")
                    }
                }
            }
        }
        .navigationTitle("settings.category.active_sessions")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            viewModel.dependencyContainer = container
            await viewModel.loadSessions()
        }
        .refreshable {
            await viewModel.loadSessions()
        }
        .navigationDestination(isPresented: $showFastAuthScanner) {
            FastAuthScannerView(isPresented: $showFastAuthScanner)
        }
        .onChange(of: showFastAuthScanner) { _, isPresented in
            if !isPresented {
                Task { await viewModel.loadSessions() }
            }
        }
        .confirmationDialog(
            "settings.sessions.terminate_all_others.confirm_title",
            isPresented: $showTerminateAllConfirm,
            titleVisibility: .visible
        ) {
            Button("settings.sessions.terminate_all_others.confirm", role: .destructive) {
                Task { await viewModel.terminateAllOtherSessions() }
            }
            Button("common.cancel", role: .cancel) {}
        }
    }
}

private struct SessionRow: View {
    let session: SessionInfo
    let isCurrent: Bool
    let onTerminate: () -> Void

    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text(session.displayName.isEmpty
                         ? String(localized: "settings.sessions.device_default")
                         : session.displayName)
                        .font(.body)
                    if isCurrent {
                        Text("settings.sessions.current_short")
                            .font(.caption2)
                            .padding(.horizontal, 6)
                            .padding(.vertical, 2)
                            .background(Color.accentColor.opacity(0.15))
                            .foregroundStyle(Color.accentColor)
                            .clipShape(Capsule())
                    }
                }
                if !session.appName.isEmpty || !session.operationSystem.isEmpty {
                    Text([session.appName, session.operationSystem].filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Text(session.createdAt, style: .relative)
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }

            Spacer()

            if !isCurrent {
                Button(role: .destructive) {
                    onTerminate()
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .foregroundStyle(.red)
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.vertical, 4)
    }
}
