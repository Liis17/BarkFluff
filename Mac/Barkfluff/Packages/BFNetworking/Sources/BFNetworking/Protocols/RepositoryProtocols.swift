//
//  RepositoryProtocols.swift
//  BFNetworking
//
//  Протоколы репозиториев
//

import Foundation

// MARK: - Beacon

public protocol BeaconRepositoryProtocol: Sendable {
    func getServerInfo(host: String, port: Int) async throws -> ServerInfoDTO
}

// MARK: - Identity

public protocol IdentityRepositoryProtocol: Sendable {
    /// Логин (username или email + пароль + опциональный OTP код)
    func auth(login: String, isEmail: Bool, password: String, otpCode: String?) async throws -> AuthTokens

    /// Обновить access token по refresh token
    func createToken(refreshToken: String) async throws -> TokenInfo

    /// Регистрация аккаунта. Возвращает code_id.
    func createAccount(firstName: String, lastName: String, username: String, email: String) async throws -> String

    /// Подтвердить регистрацию. Возвращает ТОЛЬКО refresh_token.
    func confirmAccount(codeID: String, code: String) async throws -> TokenInfo

    // Методы, не входящие в скоуп авторизации (заглушки)
    func getActiveSessions() async throws -> [SessionInfo]
    func removeActiveSession(deviceID: String) async throws
    func enableOTP() async throws -> OTPSetupInfo
    func confirmOTP(code: String) async throws
    func disableOTP(code: String) async throws
    func listOTP() async throws -> OTPStatus
    func resetPassword(email: String) async throws -> String
    func confirmResetPassword(codeID: String, code: String, newPassword: String) async throws
    func setPassword(password: String) async throws
    func logout() async throws
}

// MARK: - Users

public protocol UsersRepositoryProtocol: Sendable {
    func getUser(userID: Int64) async throws -> UserInfo
    func getCurrentUser() async throws -> UserInfo
    func changeName(firstName: String, lastName: String) async throws
    func changeUsername(newUsername: String) async throws
    func changeBio(newBio: String) async throws
    func setProfilePicture(fileID: String) async throws
    func checkExistUsername(username: String) async throws -> Bool
    func checkExistEmail(email: String) async throws -> Bool
    func searchUsers(query: String, offset: Int32, size: Int32) async throws -> (users: [UserInfo], totalCount: Int32)
    func getUserBadges(userID: Int64) async throws -> [UserBadgeInfo]
}

// MARK: - Messages

public protocol MessagesRepositoryProtocol: Sendable {
    func listChats(offset: Int32, size: Int32) async throws -> (chats: [ChatInfo], totalCount: Int32)
    func listMessages(chatID: String, fromMessageID: Int64, offsetBefore: Int32, offsetAfter: Int32) async throws -> [MessageInfo]
    func sendMessage(chatID: String?, userID: Int64?, text: String, fileIDs: [String], forwardedMessageID: Int64?) async throws -> MessageInfo
    func createGroupChat(userIDs: [Int64], title: String, pictureFileID: String?) async throws -> ChatInfo
    func kickUser(chatID: String, userID: Int64) async throws
    func markAsRead(messageIDs: [Int64]) async throws
    func listChatMembers(chatID: String, offset: Int32, size: Int32) async throws -> (members: [ChatMemberInfo], totalCount: Int32)

    /// Получить список вложений в чате
    /// - Parameters:
    ///   - chatID: ID чата
    ///   - offset: Смещение для пагинации
    ///   - size: Количество элементов (макс 50)
    ///   - filter: Фильтр по типу (nil = все типы)
    ///   - sortDescending: true = новые первыми
    /// - Returns: Список вложений и общее количество
    func listChatAttachments(
        chatID: String,
        offset: Int32,
        size: Int32,
        filter: AttachmentType?,
        sortDescending: Bool
    ) async throws -> (attachments: [ChatAttachmentInfo], totalCount: Int32)

    /// Получить ID личного чата с пользователем (nil если чат не существует)
    func getPersonChatId(userID: Int64) async throws -> String?
}

// MARK: - Files

public protocol FilesRepositoryProtocol: Sendable {
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
    func getTempDownloadURL(fileID: String) async throws -> String

    /// Получить временные URL для скачивания нескольких файлов
    func getTempDownloadURLs(fileIDs: [String]) async throws -> [FileDownloadInfo]

    /// Проверить хеш файла на дедупликацию
    func checkFileHash(hash: String) async throws -> FileCheckResult

    /// Получить информацию о хранилище пользователя
    func getUserStorageInfo() async throws -> StorageInfo
}

// MARK: - Updates

public protocol UpdatesRepositoryProtocol: Sendable {
    func subscribeNewMessages() async throws -> AsyncThrowingStream<NewMessageEvent, Error>
    func subscribeMessagesRead() async throws -> AsyncThrowingStream<MessageReadEvent, Error>
}

// MARK: - FastAuth

public protocol FastAuthRepositoryProtocol: Sendable {
    func generateFastAuthToken(type: FastAuthType) async throws -> FastAuthTokenInfo
    func checkFastAuth(fastAuthID: String) async throws -> FastAuthStatus
    func acceptFastAuth(fastAuthID: String) async throws
    func subscribeFastAuthResult(fastAuthID: String) async throws -> AsyncThrowingStream<FastAuthResult, Error>
    func subscribeFastAuthRequests() async throws -> AsyncThrowingStream<FastAuthRequest, Error>
    func connectDevice(deviceToken: String) async throws
    func acceptConnectDevice(deviceID: String) async throws
    func subscribeConnectDeviceStatus() async throws -> AsyncThrowingStream<ConnectDeviceStatus, Error>
    func listConnectedDevices() async throws -> [ConnectedDeviceInfo]
    func generateConnectDeviceToken() async throws -> String
}

// MARK: - Navigator

public protocol NavigatorRepositoryProtocol: Sendable {
    /// Получить список доступных серверов из глобального реестра Navigator
    func listServers() async throws -> [NavigatorServerInfo]

    /// Зарегистрировать сервер в Navigator (для админ-клиентов)
    func registerServer(
        name: String,
        host: String,
        port: Int,
        description: String?,
        accountsCount: Int64,
        serverPublicName: String
    ) async throws
}

// MARK: - Onliner

public protocol OnlinerRepositoryProtocol: Sendable {
    /// Установить статус "В сети" (heartbeat)
    func setOnlineStatus() async throws

    /// Получить текущие статусы пользователей (batch)
    func getOnlineStatus(userIDs: [Int64]) async throws -> [UserOnlineStatusInfo]

    /// Подписаться на изменения статусов (server streaming)
    func subscribeToOnlineStatus(userIDs: [Int64]) async throws -> AsyncThrowingStream<UserOnlineStatusInfo, Error>

    /// Изменить список отслеживаемых пользователей в активной подписке
    func changeUsersInSubscription(userIDs: [Int64]) async throws
}

/// Информация об онлайн-статусе пользователя (DTO из репозитория)
public struct UserOnlineStatusInfo: Sendable, Hashable {
    public let userID: Int64
    public let status: UserOnlineStatusType
    public let lastSeen: Date?

    public init(userID: Int64, status: UserOnlineStatusType, lastSeen: Date?) {
        self.userID = userID
        self.status = status
        self.lastSeen = lastSeen
    }
}

/// Тип онлайн-статуса (DTO)
public enum UserOnlineStatusType: Sendable, Hashable {
    case online
    case offline
    case unknown
}
