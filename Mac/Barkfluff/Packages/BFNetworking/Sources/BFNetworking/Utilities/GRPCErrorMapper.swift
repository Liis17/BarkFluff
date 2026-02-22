//
//  GRPCErrorMapper.swift
//  BFNetworking
//
//  Маппинг gRPC ошибок в доменные
//

import Foundation
import GRPCCore

/// Маппер gRPC RPCError в BFNetworkingError
public enum GRPCErrorMapper {

    /// Преобразовать gRPC RPCError в BFNetworkingError
    public static func map(_ error: RPCError) -> BFNetworkingError {
        let code = error.code
        let message = error.message

        switch code {
        case .unauthenticated:
            return .sessionExpired

        case .invalidArgument:
            let msg = message.lowercased()
            if msg.contains("otp") || msg.contains("2fa") || msg.contains("code needed") {
                return .otpRequired
            }
            if msg.contains("password") {
                return .invalidCredentials
            }
            return .invalidArgument(message)

        case .alreadyExists:
            let msg = message.lowercased()
            if msg.contains("username") {
                return .usernameAlreadyExists
            }
            if msg.contains("email") {
                return .emailAlreadyExists
            }
            return .alreadyExists(message)

        case .notFound:
            return .notFound(message)

        case .unavailable:
            return .serverUnavailable

        case .deadlineExceeded:
            return .timeout

        case .permissionDenied:
            return .unauthorized

        default:
            return .unknown(message)
        }
    }

    /// Проверить, требует ли ошибка повторной авторизации
    public static func requiresReauth(_ error: Error) -> Bool {
        if let rpcError = error as? RPCError {
            return rpcError.code == .unauthenticated
        }
        if let bfError = error as? BFNetworkingError {
            switch bfError {
            case .unauthorized, .sessionExpired:
                return true
            default:
                return false
            }
        }
        return false
    }

    /// Проверить, можно ли повторить запрос
    public static func isRetryable(_ error: Error) -> Bool {
        guard let rpcError = error as? RPCError else { return false }
        switch rpcError.code {
        case .unavailable, .deadlineExceeded, .resourceExhausted, .aborted:
            return true
        default:
            return false
        }
    }
}
