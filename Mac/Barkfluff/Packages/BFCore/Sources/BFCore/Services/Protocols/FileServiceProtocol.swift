//
//  FileServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса файлов
//

import Foundation
import BFNetworking

/// Протокол сервиса файлов
public protocol FileServiceProtocol: Sendable {
    /// Получить URL для загрузки файла
    func getUploadURL(fileType: UploadFileType) async throws -> FileUploadInfo

    /// Загрузить файл с дедупликацией по SHA256
    /// - Parameters:
    ///   - data: Данные файла
    ///   - fileName: Имя файла
    ///   - fileType: Тип файла (опционально, определяется по расширению)
    /// - Returns: ID загруженного файла
    func uploadFile(data: Data, fileName: String, fileType: UploadFileType?) async throws -> String

    /// Получить временный URL для скачивания файла
    func getDownloadURL(fileID: String) async throws -> String

    /// Получить временные URL для скачивания нескольких файлов
    func getDownloadURLs(fileIDs: [String]) async throws -> [FileDownloadInfo]

    /// Проверить хеш файла на дедупликацию
    func checkFileHash(hash: String) async throws -> FileCheckResult

    /// Получить информацию о хранилище
    func getStorageInfo() async throws -> StorageInfo

    /// Вычислить SHA256 хеш данных
    static func computeHash(data: Data) -> String
}
