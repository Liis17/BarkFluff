//
//  FileService.swift
//  BFCore
//
//  Реализация сервиса файлов
//

import Foundation
import BFNetworking
import CryptoKit

/// Реализация сервиса файлов
public actor FileService: FileServiceProtocol {

    private let filesRepository: FilesRepositoryProtocol
    private let fileURLCache: FileURLCache

    public init(filesRepository: FilesRepositoryProtocol, fileURLCache: FileURLCache = FileURLCache()) {
        self.filesRepository = filesRepository
        self.fileURLCache = fileURLCache
    }

    // MARK: - FileServiceProtocol

    public func getUploadURL(fileType: UploadFileType) async throws -> FileUploadInfo {
        let info = try await filesRepository.getUploadURL(fileType: fileType)
        return FileUploadInfo(fileID: info.fileID, uploadURL: info.uploadURL, expiresIn: info.expiresIn)
    }

    public func uploadFile(data: Data, fileName: String, fileType: UploadFileType?) async throws -> String {
        try await filesRepository.uploadFile(data: data, fileName: fileName, fileType: fileType)
    }

    public func getDownloadURL(fileID: String) async throws -> String {
        if let cached = await fileURLCache.getURL(forFileID: fileID) {
            return cached
        }
        let url = try await filesRepository.getTempDownloadURL(fileID: fileID)
        await fileURLCache.setURL(url, forFileID: fileID)
        return url
    }

    public func getDownloadURLs(fileIDs: [String]) async throws -> [FileDownloadInfo] {
        try await filesRepository.getTempDownloadURLs(fileIDs: fileIDs)
    }

    public func checkFileHash(hash: String) async throws -> FileCheckResult {
        let result = try await filesRepository.checkFileHash(hash: hash)
        return FileCheckResult(exists: result.exists, fileID: result.fileID)
    }

    public func getStorageInfo() async throws -> StorageInfo {
        let info = try await filesRepository.getUserStorageInfo()
        return StorageInfo(usedBytes: info.usedBytes, limitBytes: info.limitBytes)
    }

    /// Вычисляет SHA256 хеш данных (lowercase hex)
    public static func computeHash(data: Data) -> String {
        let hash = SHA256.hash(data: data)
        return hash.compactMap { String(format: "%02x", $0) }.joined()
    }
}
