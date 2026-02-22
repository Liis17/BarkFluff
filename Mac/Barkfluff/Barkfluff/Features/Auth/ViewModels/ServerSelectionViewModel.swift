//
//  ServerSelectionViewModel.swift
//  Barkfluff
//
//  ViewModel для экрана выбора сервера
//

import SwiftUI
import Observation
import BFCore

@Observable
final class ServerSelectionViewModel {

    // MARK: - Список серверов (из Navigator)

    /// Состояние загрузки списка серверов
    enum ServerListState {
        case loading
        case loaded([NavigatorServer])
        case empty
        case error(String)
    }

    var serverListState: ServerListState = .loading

    /// ID сервера, к которому сейчас подключаемся (nil если нет активного подключения)
    var connectingServerID: String?

    // MARK: - Ручной ввод (существующая логика)

    var serverAddress = ""
    var isManualSectionExpanded = false

    // MARK: - Общее

    var isLoading = false          // для ручного подключения
    var errorMessage: String?      // общая ошибка подключения

    // MARK: - Dependencies

    private let serverDiscoveryService: ServerDiscoveryServiceProtocol
    private let coordinator: AppCoordinator

    init(serverDiscoveryService: ServerDiscoveryServiceProtocol, coordinator: AppCoordinator) {
        self.serverDiscoveryService = serverDiscoveryService
        self.coordinator = coordinator
    }

    // MARK: - Загрузка списка серверов

    /// Вызывается при появлении экрана
    func loadServers() async {
        serverListState = .loading

        do {
            let servers = try await serverDiscoveryService.listServers()
            if servers.isEmpty {
                serverListState = .empty
                isManualSectionExpanded = true  // Раскрыть ручной ввод если серверов нет
            } else {
                serverListState = .loaded(servers)
            }
        } catch {
            serverListState = .error("Не удалось загрузить список серверов")
            isManualSectionExpanded = true  // Раскрыть ручной ввод при ошибке
        }
    }

    // MARK: - Подключение к серверу из списка

    /// Подключиться к серверу из списка Navigator
    func connectToServer(_ server: NavigatorServer) async {
        connectingServerID = server.id
        errorMessage = nil

        do {
            _ = try await serverDiscoveryService.connect(host: server.host, port: server.port)
            // Успех — переход к авторизации
            coordinator.currentState = .authentication
            coordinator.authScreen = .login
        } catch {
            errorMessage = "Не удалось подключиться к \(server.displayName): \(error.localizedDescription)"
        }

        connectingServerID = nil
    }

    // MARK: - Ручное подключение (существующая логика)

    /// Подключиться по введённому адресу
    func connectManually() async {
        isLoading = true
        errorMessage = nil

        let (host, port) = parseAddress(serverAddress)

        do {
            let serverInfo = try await serverDiscoveryService.connect(host: host, port: port)
            coordinator.currentState = .authentication
            coordinator.authScreen = .login
        } catch {
            errorMessage = error.localizedDescription
        }

        isLoading = false
    }

    // MARK: - Helpers

    /// Есть ли активное подключение (к серверу из списка ИЛИ ручное)
    var isAnyConnectionInProgress: Bool {
        connectingServerID != nil || isLoading
    }

    private func parseAddress(_ address: String) -> (host: String, port: Int) {
        let trimmed = address.trimmingCharacters(in: .whitespacesAndNewlines)
        let parts = trimmed.split(separator: ":", maxSplits: 1)
        let host = String(parts[0])
        let port = parts.count > 1 ? Int(parts[1]) ?? 7004 : 7004
        return (host, port)
    }
}
