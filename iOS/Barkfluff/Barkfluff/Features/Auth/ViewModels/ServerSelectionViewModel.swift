//
//  ServerSelectionViewModel.swift
//  Barkfluff
//
//  ViewModel для экрана выбора сервера (iOS версия)
//

import SwiftUI
import Observation
import BFCore

@Observable
final class ServerSelectionViewModel {

    // MARK: - Server List State

    /// Состояние загрузки списка серверов
    enum ServerListState {
        case loading
        case loaded([NavigatorServer])
        case empty
        case error(LocalizedStringResource)
    }

    var serverListState: ServerListState = .loading

    /// ID сервера, к которому сейчас подключаемся (nil если нет активного подключения)
    var connectingServerID: String?

    // MARK: - Manual Connection

    var serverAddress = ""

    /// Раскрыта ли секция «Ввести адрес вручную». Раскрывается автоматически
    /// при ошибке загрузки списка или пустом списке — иначе пользователь
    /// не сможет подключиться без серверов навигатора.
    var isManualSectionExpanded: Bool = false

    // MARK: - General

    var isLoading = false
    var errorMessage: LocalizedStringResource?

    // MARK: - Computed

    var isAnyConnectionInProgress: Bool {
        connectingServerID != nil || isLoading
    }

    // MARK: - Dependencies

    private let serverDiscoveryService: ServerDiscoveryServiceProtocol
    private let coordinator: AppCoordinator

    init(serverDiscoveryService: ServerDiscoveryServiceProtocol, coordinator: AppCoordinator) {
        self.serverDiscoveryService = serverDiscoveryService
        self.coordinator = coordinator
    }

    // MARK: - Load Servers

    func loadServers() async {
        serverListState = .loading

        do {
            let servers = try await serverDiscoveryService.listServers()
            if servers.isEmpty {
                serverListState = .empty
                isManualSectionExpanded = true
            } else {
                serverListState = .loaded(servers)
            }
        } catch {
            serverListState = .error(LocalizedStringResource("auth.server_selection.load_error"))
            isManualSectionExpanded = true
        }
    }

    // MARK: - Connect to Server from List

    func connectToServer(_ server: NavigatorServer) async {
        connectingServerID = server.id
        errorMessage = nil

        do {
            _ = try await serverDiscoveryService.connect(host: server.host, port: server.port)
            coordinator.currentState = .authentication
            coordinator.authScreen = .login
        } catch {
            errorMessage = LocalizedStringResource(
                "auth.server_selection.connect_failed \(server.displayName) \(error.localizedDescription)"
            )
        }

        connectingServerID = nil
    }

    // MARK: - Manual Connection

    func connectManually() async {
        guard !serverAddress.isEmpty else { return }

        isLoading = true
        errorMessage = nil

        let (host, port) = parseAddress(serverAddress)

        do {
            _ = try await serverDiscoveryService.connect(host: host, port: port)
            coordinator.currentState = .authentication
            coordinator.authScreen = .login
        } catch {
            // Сообщение об ошибке приходит из сервиса — оборачиваем как есть.
            errorMessage = LocalizedStringResource(stringLiteral: error.localizedDescription)
        }

        isLoading = false
    }

    // MARK: - Helpers

    private func parseAddress(_ address: String) -> (host: String, port: Int) {
        let trimmed = address.trimmingCharacters(in: .whitespacesAndNewlines)
        let parts = trimmed.split(separator: ":", maxSplits: 1)
        let host = String(parts[0])
        let port = parts.count > 1 ? Int(parts[1]) ?? 7004 : 7004
        return (host, port)
    }
}
