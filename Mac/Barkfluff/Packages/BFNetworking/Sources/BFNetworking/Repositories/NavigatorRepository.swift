//
//  NavigatorRepository.swift
//  BFNetworking
//
//  Репозиторий для работы с Navigator API
//

import Foundation
import GRPCCore
import GRPCNIOTransportHTTP2Posix
import GRPCProtobuf
import BFProto

/// Репозиторий для работы с Navigator API
/// Navigator — глобальный сервис, не привязанный к конкретному BarkFluff-серверу.
/// Подключение к Navigator НЕ проходит через ConnectionManager.
public actor NavigatorRepository: NavigatorRepositoryProtocol {

    /// Адрес Navigator по умолчанию
    public static let defaultHost = "navigator.barkfluff.com"
    public static let defaultPort = 443

    private let host: String
    private let port: Int

    public init(
        host: String = NavigatorRepository.defaultHost,
        port: Int = NavigatorRepository.defaultPort
    ) {
        self.host = host
        self.port = port
    }

    // MARK: - NavigatorRepositoryProtocol

    public func listServers() async throws -> [NavigatorServerInfo] {
        // Создаём транспорт к Navigator
        let transport = try HTTP2ClientTransport.Posix(
            target: .dns(host: host, port: port),
            transportSecurity: .tls
        )

        // Используем withGRPCClient для автоматического управления жизненным циклом
        let response = try await withGRPCClient(transport: transport, interceptors: []) { grpcClient in
            let navigatorClient = Barkfluff_Navigator_NavigatorApi.Client(wrapping: grpcClient)
            return try await navigatorClient.listServers(
                Barkfluff_Navigator_ListServersRequest()
            )
        }

        // Маппинг proto → доменная модель
        return response.servers.map { server in
            NavigatorServerInfo(
                name: server.name,
                description: server.description_p,
                accountsCount: server.accountsCount,
                host: cleanHost(server.beaconUri.host),
                port: Int(server.beaconUri.port),
                serverPublicName: server.serverPublicName,
                location: server.location
            )
        }
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

    public func registerServer(
        name: String,
        host: String,
        port: Int,
        description: String?,
        accountsCount: Int64,
        serverPublicName: String
    ) async throws {
        // Создаём транспорт к Navigator
        let transport = try HTTP2ClientTransport.Posix(
            target: .dns(host: self.host, port: self.port),
            transportSecurity: .tls
        )

        // Формируем запрос
        var serverInfo = Barkfluff_Navigator_ServerInfo()
        serverInfo.name = name
        serverInfo.description_p = description ?? ""
        serverInfo.accountsCount = accountsCount
        serverInfo.serverPublicName = serverPublicName

        var endpoint = Barkfluff_Navigator_ServiceEndpoint()
        endpoint.host = host
        endpoint.port = Int32(port)
        serverInfo.beaconUri = endpoint

        var request = Barkfluff_Navigator_RegisterServerRequest()
        request.server = serverInfo

        // Отправляем запрос
        _ = try await withGRPCClient(transport: transport, interceptors: []) { grpcClient in
            let navigatorClient = Barkfluff_Navigator_NavigatorApi.Client(wrapping: grpcClient)
            return try await navigatorClient.registerServer(request)
        }
    }
}
