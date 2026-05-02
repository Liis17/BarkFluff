//
//  ServerSelectionView.swift
//  Barkfluff
//
//  Экран выбора сервера
//

import SwiftUI
import BFCore

struct ServerSelectionView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: ServerSelectionViewModel?
    @State private var appeared = false

    var body: some View {
        ZStack {
            serverBackground

            ScrollView {
                VStack(spacing: Theme.Spacing.xl) {
                    headerSection

                    if let viewModel {
                        serverListSection(viewModel)
                        manualSection(viewModel)

                        if let error = viewModel.errorMessage {
                            ErrorBannerView(message: error)
                                .frame(maxWidth: 440)
                                .transition(.move(edge: .top).combined(with: .opacity))
                        }
                    }
                }
                .padding(.vertical, Theme.Spacing.xxl)
                .frame(maxWidth: .infinity)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear {
            if viewModel == nil {
                viewModel = ServerSelectionViewModel(
                    serverDiscoveryService: container.serverDiscoveryService,
                    coordinator: coordinator
                )
            }
            appeared = false
            Task {
                try? await Task.sleep(for: .milliseconds(40))
                appeared = true
            }
        }
        .task { await viewModel?.loadServers() }
    }

    // MARK: - Background

    private var serverBackground: some View {
        LinearGradient(
            colors: [
                Color.accentColor.opacity(0.14),
                Color.accentColor.opacity(0.04),
                Color(nsColor: .windowBackgroundColor)
            ],
            startPoint: .topTrailing,
            endPoint: .bottomLeading
        )
        .ignoresSafeArea()
    }

    // MARK: - Header

    private var headerSection: some View {
        VStack(spacing: Theme.Spacing.md) {
            ZStack {
                Circle()
                    .fill(Color.accentColor.opacity(0.12))
                    .frame(width: 96, height: 96)
                Circle()
                    .fill(Color.accentColor.opacity(0.07))
                    .frame(width: 76, height: 76)
                Image(systemName: "network")
                    .font(.system(size: 36, weight: .medium))
                    .foregroundStyle(Color.accentColor)
                    .symbolRenderingMode(.hierarchical)
            }
            .scaleEffect(appeared ? 1 : 0.7)
            .opacity(appeared ? 1 : 0)
            .animation(.spring(response: 0.5, dampingFraction: 0.7), value: appeared)

            VStack(spacing: 3) {
                Text("Выбор сервера")
                    .font(.title.bold())
                Text("Подключитесь к серверу BarkFluff")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            .opacity(appeared ? 1 : 0)
            .offset(y: appeared ? 0 : 6)
            .animation(.easeOut(duration: 0.4).delay(0.1), value: appeared)
        }
    }

    // MARK: - Server List

    @ViewBuilder
    private func serverListSection(_ viewModel: ServerSelectionViewModel) -> some View {
        VStack(spacing: Theme.Spacing.sm) {
            switch viewModel.serverListState {
            case .loading:
                VStack(spacing: Theme.Spacing.md) {
                    ProgressView()
                        .controlSize(.large)
                    Text("Поиск серверов...")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: 440)
                .padding(.vertical, Theme.Spacing.xl)

            case .loaded(let servers):
                VStack(spacing: Theme.Spacing.xs) {
                    ForEach(Array(servers.enumerated()), id: \.element.id) { index, server in
                        ServerCardView(
                            server: server,
                            isConnecting: viewModel.connectingServerID == server.id,
                            isDisabled: viewModel.isAnyConnectionInProgress
                                && viewModel.connectingServerID != server.id,
                            onTap: { Task { await viewModel.connectToServer(server) } }
                        )
                        .opacity(appeared ? 1 : 0)
                        .offset(y: appeared ? 0 : 8)
                        .animation(
                            .easeOut(duration: 0.35).delay(0.15 + Double(index) * 0.06),
                            value: appeared
                        )
                    }
                }
                .frame(maxWidth: 440)

            case .empty:
                VStack(spacing: Theme.Spacing.sm) {
                    Image(systemName: "server.rack")
                        .font(.title2)
                        .foregroundStyle(.tertiary)
                    Text("Нет доступных серверов")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                    Text("Подключитесь вручную ниже")
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                }
                .frame(maxWidth: 440)
                .padding(.vertical, Theme.Spacing.xl)

            case .error(let message):
                VStack(spacing: Theme.Spacing.md) {
                    Image(systemName: "exclamationmark.triangle.fill")
                        .font(.title2)
                        .foregroundStyle(.orange)
                    Text(message)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                    Button("Попробовать снова") {
                        Task { await viewModel.loadServers() }
                    }
                    .buttonStyle(.glass)
                }
                .frame(maxWidth: 440)
                .padding(.vertical, Theme.Spacing.xl)
            }
        }
    }

    // MARK: - Manual Input

    @ViewBuilder
    private func manualSection(_ viewModel: ServerSelectionViewModel) -> some View {
        VStack(spacing: Theme.Spacing.md) {
            // Разделитель
            HStack(spacing: Theme.Spacing.md) {
                Rectangle().fill(.separator).frame(height: 1)
                Text("или вручную")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
                    .fixedSize()
                Rectangle().fill(.separator).frame(height: 1)
            }
            .frame(maxWidth: 440)

            // Карточка ручного ввода
            VStack(spacing: Theme.Spacing.md) {
                HStack(spacing: Theme.Spacing.sm) {
                    Image(systemName: "link")
                        .foregroundStyle(Color.accentColor)
                        .frame(width: 20)

                    TextField("beacon.myserver.com:443", text: Bindable(viewModel).serverAddress)
                        .textFieldStyle(.roundedBorder)
                        .controlSize(.large)
                        .onSubmit {
                            guard isAddressComplete(viewModel.serverAddress),
                                  !viewModel.isAnyConnectionInProgress else { return }
                            Task { await viewModel.connectManually() }
                        }
                }

                // Кнопка появляется когда введены и хост и порт
                if isAddressComplete(viewModel.serverAddress) || viewModel.isLoading {
                    ZStack {
                        Button("Подключиться") {
                            Task { await viewModel.connectManually() }
                        }
                        .buttonStyle(.glassProminent)
                        .controlSize(.large)
                        .frame(maxWidth: .infinity)
                        .disabled(viewModel.isAnyConnectionInProgress)
                        .opacity(viewModel.isLoading ? 0 : 1)

                        if viewModel.isLoading {
                            ProgressView()
                                .progressViewStyle(.circular)
                                .controlSize(.regular)
                        }
                    }
                    .frame(height: 36)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                }
            }
            .padding(Theme.Spacing.xl)
            .background(
                RoundedRectangle(cornerRadius: 16)
                    .fill(.regularMaterial)
                    .shadow(color: .black.opacity(0.06), radius: 16, x: 0, y: 6)
                    .shadow(color: .black.opacity(0.03), radius: 3, x: 0, y: 1)
            )
            .animation(.easeInOut(duration: 0.22), value: isAddressComplete(viewModel.serverAddress))
            .frame(maxWidth: 440)
            .opacity(appeared ? 1 : 0)
            .offset(y: appeared ? 0 : 8)
            .animation(.easeOut(duration: 0.4).delay(0.3), value: appeared)
        }
    }

    // MARK: - Helpers

    /// Адрес считается полным если введены и хост, и числовой порт (формат host:port)
    private func isAddressComplete(_ address: String) -> Bool {
        let trimmed = address.trimmingCharacters(in: .whitespaces)
        guard let colonIdx = trimmed.lastIndex(of: ":") else { return false }
        let portStr = String(trimmed[trimmed.index(after: colonIdx)...])
        let host = String(trimmed[..<colonIdx])
        return !host.isEmpty && Int(portStr) != nil
    }
}

#Preview {
    ServerSelectionView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
