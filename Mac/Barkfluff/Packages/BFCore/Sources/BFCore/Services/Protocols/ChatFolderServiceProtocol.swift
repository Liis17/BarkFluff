//
//  ChatFolderServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса папок чатов.
//

import Foundation

public protocol ChatFolderServiceProtocol: Sendable {
    /// Прочитать локально кешированные папки (быстро, для cold-start).
    func loadCached() async -> [ChatFolder]

    /// Запросить актуальные папки с сервера и обновить кеш.
    func refresh() async throws -> [ChatFolder]

    /// Создать новую папку без чатов.
    func create(name: String, icon: String) async throws -> ChatFolder

    /// Обновить папку (любые поля nil не трогаются). Если `chatIDs != nil`, список чатов будет заменён.
    func update(folderID: String, name: String?, icon: String?, chatIDs: [String]?) async throws -> ChatFolder

    /// Удалить папку (чаты остаются).
    func delete(folderID: String) async throws

    /// Добавить чат в папку (идемпотентно).
    func addChat(folderID: String, chatID: String) async throws -> ChatFolder

    /// Удалить чат из папки (идемпотентно).
    func removeChat(folderID: String, chatID: String) async throws -> ChatFolder

    /// Применить новый порядок (массив `orders` уже отсортирован в нужном порядке).
    func reorder(_ orders: [(folderID: String, sortOrder: Int32)]) async throws
}
