//
//  ChatServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса чатов
//

import Foundation

/// Протокол сервиса чатов
public protocol ChatServiceProtocol: Sendable {
    /// Получить список чатов с пагинацией
    func listChats(offset: Int32, size: Int32) async throws -> PagedResult<Chat>

    /// Получить список участников чата
    func listChatMembers(chatID: String, offset: Int32, size: Int32) async throws -> PagedResult<DetailedChatMember>

    /// Создать групповой чат
    func createGroupChat(userIDs: [Int64], title: String, pictureFileID: String?) async throws -> Chat

    /// Исключить пользователя из чата
    func kickUser(chatID: String, userID: Int64) async throws

    /// Получить ID личного чата с пользователем (nil если чат не существует)
    func getPersonChatId(userID: Int64) async throws -> String?
}
