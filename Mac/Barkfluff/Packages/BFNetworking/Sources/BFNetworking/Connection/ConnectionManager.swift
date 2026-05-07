// swiftlint:disable all
//
//  ConnectionManager.swift
//  BFNetworking
//
//  Управление gRPC соединениями к сервисам BarkFluff
//

import Foundation
import GRPCCore
import GRPCNIOTransportHTTP2Posix
import GRPCProtobuf
import BFProto

// MARK: - Service Types

public enum ServiceKind: String, CaseIterable, Sendable, Codable {
    case beacon
    case identity
    case users
    case messages
    case files
    case updates
    case fastauth
    case navigator
    case configuration
    case onliner
}

public struct ServiceEndpoint: Sendable, Codable, Equatable {
    public let host: String
    public let port: Int
    public let useTLS: Bool

    public init(host: String, port: Int, useTLS: Bool = false) {
        self.host = host
        self.port = port
        self.useTLS = useTLS
    }
}

// MARK: - Connection Manager

/// Управляет gRPC соединениями к сервисам BarkFluff
public actor ConnectionManager {

    private var serviceEndpoints: [ServiceKind: ServiceEndpoint] = [:]
    public private(set) var serverInfo: ServerInfoDTO?
    public private(set) var isBootstrapped = false

    /// Снимок известных эндпоинтов сервисов. Возвращается копией, чтобы не делиться внутренним состоянием.
    public func endpoints() -> [ServiceKind: ServiceEndpoint] {
        serviceEndpoints
    }

    /// DeviceMetadataInterceptor — добавляет метаданные устройства во ВСЕ запросы
    private var deviceMetadataInterceptor: (any ClientInterceptor)?

    /// AuthInterceptor для авторизованных запросов (устанавливается после инициализации)
    private var authInterceptor: (any ClientInterceptor)?

    public init() {}

    // MARK: - Configuration

    /// Установить DeviceMetadataInterceptor для всех клиентов
    public func setDeviceMetadataInterceptor(_ interceptor: any ClientInterceptor) {
        self.deviceMetadataInterceptor = interceptor
    }

    /// Установить AuthInterceptor для авторизованных клиентов
    public func setAuthInterceptor(_ interceptor: any ClientInterceptor) {
        self.authInterceptor = interceptor
    }

    /// Базовые интерсепторы (метаданные устройства), применяются ко всем запросам
    private var baseInterceptors: [any ClientInterceptor] {
        var result: [any ClientInterceptor] = []
        if let deviceMetadataInterceptor {
            result.append(deviceMetadataInterceptor)
        }
        return result
    }

    // MARK: - Bootstrap

    /// Bootstrap через Beacon — подключиться и получить эндпоинты всех сервисов
    public func bootstrap(host: String, port: Int) async throws -> ServerInfoDTO {
        let response: Barkfluff_Beacon_GetServerInfoResponse

        do {
            // Сначала пробуем plaintext
            let transport = try HTTP2ClientTransport.Posix(
                target: .dns(host: host, port: port),
                transportSecurity: .plaintext
            )

            let interceptors = baseInterceptors
            response = try await withGRPCClient(transport: transport, interceptors: interceptors) { grpcClient in
                let beaconClient = Barkfluff_Beacon_BeaconApi.Client(wrapping: grpcClient)
                return try await beaconClient.getServerInfo(
                    Barkfluff_Beacon_GetServerInfoRequest()
                )
            }
        } catch {
            // Если plaintext не сработал — пробуем TLS
            do {
                let tlsTransport = try HTTP2ClientTransport.Posix(
                    target: .dns(host: host, port: port),
                    transportSecurity: .tls()
                )

                let interceptors = baseInterceptors
                response = try await withGRPCClient(transport: tlsTransport, interceptors: interceptors) { grpcClient in
                    let beaconClient = Barkfluff_Beacon_BeaconApi.Client(wrapping: grpcClient)
                    return try await beaconClient.getServerInfo(
                        Barkfluff_Beacon_GetServerInfoRequest()
                    )
                }
            } catch let tlsError {
                // Оба варианта не сработали — выбрасываем детальную ошибку
                throw ConnectionError.connectionFailed(
                    "Не удалось подключиться к \(host):\(port). Plaintext: \(error). TLS: \(tlsError)"
                )
            }
        }

        // Маппинг сервисов из ответа Beacon
        mapServiceEndpoints(from: response)

        // Создаём ServerInfoDTO
        let info = ServerInfoDTO(
            name: response.name,
            version: "",
            description: response.description_p.isEmpty ? nil : response.description_p,
            color: response.hasColor ? response.color.mainHex : nil,
            publicName: response.publicName.isEmpty ? nil : response.publicName,
            location: response.location.isEmpty ? nil : response.location
        )
        serverInfo = info
        isBootstrapped = true

        return info
    }

    /// Убирает схему (http://, https://) и trailing slash из хоста
    private func cleanHost(_ raw: String) -> String {
        var host = raw
        if host.hasPrefix("https://") {
            host = String(host.dropFirst("https://".count))
        } else if host.hasPrefix("http://") {
            host = String(host.dropFirst("http://".count))
        }
        // Убираем trailing slash и путь
        if let slashIdx = host.firstIndex(of: "/") {
            host = String(host[host.startIndex..<slashIdx])
        }
        return host
    }

    /// Маппинг отдельных полей Beacon-ответа в serviceEndpoints
    private func mapServiceEndpoints(from response: Barkfluff_Beacon_GetServerInfoResponse) {
        serviceEndpoints.removeAll()

        if response.hasIdentity, response.identity.hasEndpoint {
            let ep = response.identity.endpoint
            serviceEndpoints[.identity] = ServiceEndpoint(
                host: cleanHost(ep.host), port: Int(ep.port), useTLS: response.identity.tlsEnabled
            )
        }
        if response.hasUsers, response.users.hasEndpoint {
            let ep = response.users.endpoint
            serviceEndpoints[.users] = ServiceEndpoint(
                host: cleanHost(ep.host), port: Int(ep.port), useTLS: response.users.tlsEnabled
            )
        }
        if response.hasFiles, response.files.hasEndpoint {
            let ep = response.files.endpoint
            serviceEndpoints[.files] = ServiceEndpoint(
                host: cleanHost(ep.host), port: Int(ep.port), useTLS: response.files.tlsEnabled
            )
        }
        if response.hasMessages, response.messages.hasEndpoint {
            let ep = response.messages.endpoint
            serviceEndpoints[.messages] = ServiceEndpoint(
                host: cleanHost(ep.host), port: Int(ep.port), useTLS: response.messages.tlsEnabled
            )
        }
        if response.hasUpdates, response.updates.hasEndpoint {
            let ep = response.updates.endpoint
            serviceEndpoints[.updates] = ServiceEndpoint(
                host: cleanHost(ep.host), port: Int(ep.port), useTLS: response.updates.tlsEnabled
            )
        }
        if response.hasOnliner, response.onliner.hasEndpoint {
            let ep = response.onliner.endpoint
            serviceEndpoints[.onliner] = ServiceEndpoint(
                host: cleanHost(ep.host), port: Int(ep.port), useTLS: response.onliner.tlsEnabled
            )
        }
    }

    // MARK: - Client Execution

    /// Выполнить операцию с публичным gRPC-клиентом (БЕЗ AuthInterceptor, С метаданными устройства).
    /// Клиент автоматически запускается и останавливается.
    public func withPublicClient<T: Sendable>(
        for kind: ServiceKind,
        operation: @Sendable (GRPCClient<HTTP2ClientTransport.Posix>) async throws -> T
    ) async throws -> T {
        guard let endpoint = serviceEndpoints[kind] else {
            throw ConnectionError.serviceNotConfigured(kind)
        }

        let transport = try HTTP2ClientTransport.Posix(
            target: .dns(host: endpoint.host, port: endpoint.port),
            transportSecurity: endpoint.useTLS ? .tls() : .plaintext
        )

        let interceptors = baseInterceptors
        return try await withGRPCClient(transport: transport, interceptors: interceptors) { client in
            try await operation(client)
        }
    }

    /// Выполнить операцию с авторизованным gRPC-клиентом (С AuthInterceptor + метаданные устройства).
    /// Клиент автоматически запускается и останавливается.
    public func withAuthorizedClient<T: Sendable>(
        for kind: ServiceKind,
        operation: @Sendable (GRPCClient<HTTP2ClientTransport.Posix>) async throws -> T
    ) async throws -> T {
        guard let endpoint = serviceEndpoints[kind] else {
            throw ConnectionError.serviceNotConfigured(kind)
        }

        var interceptors = baseInterceptors
        if let authInterceptor {
            interceptors.append(authInterceptor)
        }

        let transport = try HTTP2ClientTransport.Posix(
            target: .dns(host: endpoint.host, port: endpoint.port),
            transportSecurity: endpoint.useTLS ? .tls() : .plaintext
        )

        return try await withGRPCClient(transport: transport, interceptors: interceptors) { client in
            try await operation(client)
        }
    }

    // MARK: - Helpers

    public func getEndpoint(for kind: ServiceKind) -> ServiceEndpoint? {
        serviceEndpoints[kind]
    }

    public func setEndpoint(_ endpoint: ServiceEndpoint, for kind: ServiceKind) {
        serviceEndpoints[kind] = endpoint
    }

    public func shutdown() async {
        isBootstrapped = false
        serviceEndpoints.removeAll()
        serverInfo = nil
    }
}
