//
//  ServerSelectionView.swift
//  Barkfluff
//
//  Экран выбора сервера (iOS)
//

import SwiftUI
import BFCore

struct ServerSelectionView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: ServerSelectionViewModel?

    var body: some View {
        ScrollView {
            VStack(spacing: 24) {
                // Заголовок
                headerSection

                // Основной контент
                if let viewModel {
                    // Секция списка серверов
                    serverListSection(viewModel)

                    // Ручное подключение
                    manualConnectionSection(viewModel)

                    // Ошибка
                    if let errorMessage = viewModel.errorMessage {
                        Text(errorMessage)
                            .font(.subheadline)
                            .foregroundStyle(.red)
                    }
                }
            }
            .padding(20)
        }
        .background(Color(uiColor: .systemGroupedBackground))
        .onAppear {
            if viewModel == nil {
                viewModel = ServerSelectionViewModel(
                    serverDiscoveryService: container.serverDiscoveryService,
                    coordinator: coordinator
                )
            }
        }
        .task {
            await viewModel?.loadServers()
        }
    }

    // MARK: - Header

    private var headerSection: some View {
        VStack(spacing: 12) {
            Image(systemName: "server.rack")
                .font(.system(size: 44))
                .foregroundStyle(.blue)

            Text(verbatim: "BarkFluff")
                .font(.system(size: 24, weight: .bold))

            Text("auth.server_selection.subtitle")
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Server List Section

    @ViewBuilder
    private func serverListSection(_ viewModel: ServerSelectionViewModel) -> some View {
        switch viewModel.serverListState {
        case .loading:
            VStack(spacing: 12) {
                ProgressView()
                Text("auth.server_selection.searching")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 32)

        case .loaded(let servers):
            VStack(spacing: 8) {
                ForEach(servers) { server in
                    ServerCardView(
                        server: server,
                        isConnecting: viewModel.connectingServerID == server.id,
                        isDisabled: viewModel.isAnyConnectionInProgress
                            && viewModel.connectingServerID != server.id
                    ) {
                        Task { await viewModel.connectToServer(server) }
                    }
                }
            }

        case .empty:
            VStack(spacing: 12) {
                Image(systemName: "server.rack")
                    .font(.system(size: 36))
                    .foregroundStyle(.tertiary)

                Text("auth.server_selection.empty.title")
                    .font(.headline)

                Text("auth.server_selection.empty.hint")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 24)

        case .error(let message):
            VStack(spacing: 12) {
                Image(systemName: "wifi.exclamationmark")
                    .font(.system(size: 36))
                    .foregroundStyle(.orange)

                Text(message)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)

                Button("common.retry") {
                    Task { await viewModel.loadServers() }
                }
                .buttonStyle(.bordered)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 24)
        }
    }

    // MARK: - Manual Connection

    @ViewBuilder
    private func manualConnectionSection(_ viewModel: ServerSelectionViewModel) -> some View {
        VStack(spacing: 12) {
            // Разделитель
            HStack {
                Rectangle()
                    .fill(.secondary.opacity(0.3))
                    .frame(height: 1)
                Text("common.or")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Rectangle()
                    .fill(.secondary.opacity(0.3))
                    .frame(height: 1)
            }

            Text("auth.server_selection.manual.title")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)

            // Поле ввода адреса
            VStack(alignment: .leading, spacing: 4) {
                Text("auth.server_selection.manual.address")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                TextField("auth.server_selection.manual.placeholder", text: Bindable(viewModel).serverAddress)
                    .textFieldStyle(.roundedBorder)
                    .textContentType(.URL)
                    .autocapitalization(.none)
                    .autocorrectionDisabled()
                    .keyboardType(.URL)
                    .disabled(viewModel.isAnyConnectionInProgress)
                    .onSubmit {
                        guard !viewModel.serverAddress.isEmpty,
                              !viewModel.isAnyConnectionInProgress else { return }
                        Task { await viewModel.connectManually() }
                    }
            }

            Button {
                Task { await viewModel.connectManually() }
            } label: {
                if viewModel.isLoading {
                    ProgressView()
                        .controlSize(.small)
                } else {
                    Text("auth.server_selection.connect")
                }
            }
            .buttonStyle(.borderedProminent)
            .frame(maxWidth: .infinity)
            .disabled(viewModel.serverAddress.isEmpty || viewModel.isAnyConnectionInProgress)
        }
    }
}

// MARK: - Server Card View

struct ServerCardView: View {
    let server: NavigatorServer
    let isConnecting: Bool
    let isDisabled: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 12) {
                // Иконка сервера
                ZStack {
                    Circle()
                        .fill(serverColor.opacity(0.2))
                        .frame(width: 44, height: 44)

                    Text(String(server.displayName.prefix(1)))
                        .font(.headline)
                        .foregroundStyle(serverColor)
                }

                // Информация о сервере
                VStack(alignment: .leading, spacing: 2) {
                    Text(server.displayName)
                        .font(.headline)

                    if let description = server.description, !description.isEmpty {
                        Text(description)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }

                    Text(locationLine)
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                        .lineLimit(1)
                }

                Spacer()

                // Индикатор
                if isConnecting {
                    ProgressView()
                        .controlSize(.small)
                } else {
                    Image(systemName: "chevron.right")
                        .font(.subheadline)
                        .foregroundStyle(.tertiary)
                }
            }
            .padding(12)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background(Color(uiColor: .secondarySystemGroupedBackground))
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .disabled(isDisabled)
        .opacity(isDisabled ? 0.5 : 1)
    }

    private var locationLine: String {
        let handle = "@\(server.name)"
        if server.location.isEmpty {
            return handle
        }
        return "\(server.location) \(handle)"
    }

    private var serverColor: Color {
        let colors: [Color] = [.blue, .purple, .green, .orange, .pink, .cyan, .indigo, .mint]
        let hash = abs(server.name.hashValue)
        return colors[hash % colors.count]
    }
}

#Preview {
    NavigationStack {
        ServerSelectionView()
            .environment(AppCoordinator())
            .environment(DependencyContainer())
    }
}
