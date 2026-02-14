# Техническое задание: Доработка флоу регистрации в macOS клиенте

## 1. Анализ текущего состояния

### 1.1. Сравнение WPF и macOS клиентов

| Функция | WPF (9 шагов) | macOS (2 шага) | Статус |
|---------|---------------|----------------|--------|
| Ввод имени и фамилии | Шаг 1 | Часть Шага 1 | ✅ Есть |
| Ввод логина | Шаг 2 (+ проверка на сервере) | Часть Шага 1 | ⚠️ Без проверки |
| Ввод email | Шаг 3 | Часть Шага 1 | ✅ Есть |
| Подтверждение email | Шаг 4 (код) | Шаг 2 (код) | ✅ Есть |
| **Установка пароля** | **Шаг 5** | **❌ ОТСУТСТВУЕТ** | **❌ Критично** |
| Загрузка аватара | Шаг 6 | ❌ Отсутствует | ❌ Нужно |
| Описание профиля (bio) | Шаг 7 | ❌ Отсутствует | ❌ Нужно |
| Подключение 2FA | Шаг 8 | ❌ Отсутствует | ❌ Нужно |
| Завершение регистрации | Шаг 9 | ❌ Отсутствует | ❌ Нужно |
| Валидация полей | 5 валидаторов | ❌ Нет | ❌ Критично |
| Проверка username на сервере | ✅ CheckUsername | ❌ Нет | ❌ Нужно |
| Rate limiting кодов | ✅ 5 попыток / 15 мин | ❌ Нет | ⚠️ Желательно |
| Индикатор прогресса | ✅ "Шаг X из 9" | ❌ Нет | ⚠️ Желательно |
| Анимации переходов | ✅ Slide анимации | ❌ Нет | ⚠️ Желательно |

### 1.2. Критические проблемы

1. **Пароль НЕ устанавливается** — после регистрации пользователь не может войти!
2. **Нет валидации** — пользователь может ввести некорректные данные
3. **Нет проверки username** — можно зарегистрировать занятое имя

---

## 2. Архитектура решения

### 2.1. Новая структура флоу регистрации (9 шагов)

```
┌─────────────────────────────────────────────────────────────────┐
│                    REGISTRATION FLOW (macOS)                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Step 1: Personal Info                                          │
│  ├── FirstName (минимум 3 символа, максимум 40)                 │
│  └── LastName (опционально, максимум 40)                        │
│      ↓                                                           │
│  Step 2: Username                                               │
│  ├── Валидация локальная (3-30 символов, a-zA-Z0-9_-)          │
│  └── Проверка на сервере (CheckExistUsername)                   │
│      ↓                                                           │
│  Step 3: Email                                                  │
│  ├── Валидация формата email                                    │
│  └── API: CreateAccount → получение codeID                      │
│      ↓                                                           │
│  Step 4: Email Confirmation                                     │
│  ├── Ввод 6-значного кода                                       │
│  ├── Rate limiting (5 попыток / 15 мин кулдаун)                │
│  └── API: ConfirmAccount → получение refresh token              │
│      ↓                                                           │
│  Step 5: Password (КРИТИЧНО!)                                   │
│  ├── Минимум 8 символов                                         │
│  ├── Валидация силы пароля (score >= 60)                        │
│  ├── Подтверждение пароля                                       │
│  └── API: SetPassword                                           │
│      ↓                                                           │
│  Step 6: Avatar (опционально)                                   │
│  ├── Выбор изображения из файловой системы                      │
│  ├── Кадрирование (crop)                                        │
│  ├── API: FilesRepository.uploadFile (userAvatar)               │
│  └── API: UsersRepository.setProfilePicture                     │
│      ↓                                                           │
│  Step 7: Bio / Profile Preview                                  │
│  ├── Ввод описания профиля (опционально)                        │
│  ├── Превью профиля                                             │
│  └── API: UsersRepository.changeBio                             │
│      ↓                                                           │
│  Step 8: 2FA Setup (опционально)                                │
│  ├── QR-код для Google Authenticator                            │
│  ├── Ввод 6-значного кода                                       │
│  └── API: IdentityRepository.enableOTP + confirmOTP             │
│      ↓                                                           │
│  Step 9: Completion                                             │
│  ├── Успешное завершение                                        │
│  └── Переход на главный экран                                   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2. Новые файлы для создания

```
Mac/Barkfluff/Barkfluff/Features/Auth/
├── ViewModels/
│   ├── RegisterViewModel.swift          (обновить)
│   ├── RegistrationStateService.swift   (НОВЫЙ)
│   └── RegistrationStep.swift           (НОВЫЙ - enum шагов)
├── Views/
│   ├── RegisterView.swift               (обновить)
│   ├── Components/
│   │   ├── RegistrationStepView.swift   (НОВЫЙ)
│   │   ├── ProgressIndicatorView.swift  (НОВЫЙ)
│   │   ├── OTPInputView.swift           (НОВЫЙ)
│   │   ├── PasswordStrengthView.swift   (НОВЫЙ)
│   │   ├── AvatarCropperView.swift      (НОВЫЙ)
│   │   └── QRCodeView.swift             (НОВЫЙ)
│   └── Steps/
│       ├── Step1_PersonalInfoView.swift (НОВЫЙ)
│       ├── Step2_UsernameView.swift     (НОВЫЙ)
│       ├── Step3_EmailView.swift        (НОВЫЙ)
│       ├── Step4_ConfirmEmailView.swift (НОВЫЙ)
│       ├── Step5_PasswordView.swift     (НОВЫЙ)
│       ├── Step6_AvatarView.swift       (НОВЫЙ)
│       ├── Step7_BioView.swift          (НОВЫЙ)
│       ├── Step8_TwoFAView.swift        (НОВЫЙ)
│       └── Step9_CompletionView.swift   (НОВЫЙ)

Mac/Barkfluff/Barkfluff/Features/Auth/Validators/  (НОВАЯ ПАПКА)
├── PersonalInfoValidator.swift          (НОВЫЙ)
├── UsernameValidator.swift              (НОВЫЙ)
├── EmailValidator.swift                 (НОВЫЙ)
├── PasswordValidator.swift              (НОВЫЙ)
└── VerificationCodeValidator.swift      (НОВЫЙ)
```

---

## 3. Детальная спецификация шагов

### 3.1. RegistrationStateService (НОВЫЙ)

Хранит состояние между шагами регистрации:

```swift
// Mac/Barkfluff/Barkfluff/Features/Auth/ViewModels/RegistrationStateService.swift

import Foundation
import SwiftUI

@Observable
final class RegistrationStateService {
    // MARK: - User Data
    var firstName: String = ""
    var lastName: String = ""
    var username: String = ""
    var email: String = ""
    var codeID: String = ""
    var password: String = ""  // Важно: очищать после использования!
    var bio: String = ""
    var avatarImageData: Data?

    // MARK: - State
    var currentStep: RegistrationStep = .personalInfo
    var isLoading: Bool = false
    var errorMessage: String?

    // MARK: - Rate Limiting
    private var verificationAttempts: Int = 0
    private var verificationCooldownEnd: Date?
    private let maxVerificationAttempts = 5
    private let cooldownMinutes = 15

    var canAttemptVerification: Bool {
        guard let cooldownEnd = verificationCooldownEnd else { return true }
        return Date() >= cooldownEnd
    }

    var remainingAttempts: Int {
        max(0, maxVerificationAttempts - verificationAttempts)
    }

    var cooldownRemaining: TimeInterval? {
        guard let cooldownEnd = verificationCooldownEnd else { return nil }
        let remaining = cooldownEnd.timeIntervalSinceNow
        return remaining > 0 ? remaining : nil
    }

    // MARK: - Methods
    func recordVerificationAttempt(success: Bool) {
        if success {
            verificationAttempts = 0
            verificationCooldownEnd = nil
        } else {
            verificationAttempts += 1
            if verificationAttempts >= maxVerificationAttempts {
                verificationCooldownEnd = Date().addingTimeInterval(TimeInterval(cooldownMinutes * 60))
            }
        }
    }

    func reset() {
        firstName = ""
        lastName = ""
        username = ""
        email = ""
        codeID = ""
        password = ""
        bio = ""
        avatarImageData = nil
        currentStep = .personalInfo
        verificationAttempts = 0
        verificationCooldownEnd = nil
        errorMessage = nil
    }

    // MARK: - Progress
    var progressPercentage: Double {
        Double(currentStep.rawValue) / Double(RegistrationStep.totalSteps) * 100
    }

    var stepIndicator: String {
        "Шаг \(currentStep.rawValue) из \(RegistrationStep.totalSteps)"
    }
}

// MARK: - RegistrationStep Enum

enum RegistrationStep: Int, CaseIterable {
    case personalInfo = 1
    case username = 2
    case email = 3
    case confirmEmail = 4
    case password = 5
    case avatar = 6
    case bio = 7
    case twoFA = 8
    case completion = 9

    static let totalSteps = 9

    var title: String {
        switch self {
        case .personalInfo: return "Личная информация"
        case .username: return "Имя пользователя"
        case .email: return "Электронная почта"
        case .confirmEmail: return "Подтверждение почты"
        case .password: return "Создание пароля"
        case .avatar: return "Фото профиля"
        case .bio: return "О себе"
        case .twoFA: return "Двухфакторная аутентификация"
        case .completion: return "Завершение"
        }
    }

    var next: RegistrationStep? {
        guard let nextRaw = rawValue + 1,
              nextRaw <= Self.totalSteps else { return nil }
        return RegistrationStep(rawValue: nextRaw)
    }

    var previous: RegistrationStep? {
        guard let prevRaw = rawValue - 1,
              prevRaw >= 1 else { return nil }
        return RegistrationStep(rawValue: prevRaw)
    }
}
```

### 3.2. Валидаторы (НОВЫЕ)

#### PersonalInfoValidator

```swift
// Mac/Barkfluff/Barkfluff/Features/Auth/Validators/PersonalInfoValidator.swift

import Foundation

struct PersonalInfoValidator {
    static let minFirstNameLength = 3
    static let maxNameLength = 40

    static func validateFirstName(_ firstName: String) -> ValidationResult {
        let trimmed = firstName.trimmingCharacters(in: .whitespaces)

        guard !trimmed.isEmpty else {
            return .invalid("Имя не может быть пустым")
        }

        guard trimmed.count >= minFirstNameLength else {
            return .invalid("Имя должно содержать минимум \(minFirstNameLength) символа")
        }

        guard trimmed.count <= maxNameLength else {
            return .invalid("Имя не должно превышать \(maxNameLength) символов")
        }

        return .valid
    }

    static func validateLastName(_ lastName: String) -> ValidationResult {
        let trimmed = lastName.trimmingCharacters(in: .whitespaces)

        // Фамилия опциональна
        if trimmed.isEmpty { return .valid }

        guard trimmed.count <= maxNameLength else {
            return .invalid("Фамилия не должна превышать \(maxNameLength) символов")
        }

        return .valid
    }
}
```

#### UsernameValidator

```swift
// Mac/Barkfluff/Barkfluff/Features/Auth/Validators/UsernameValidator.swift

import Foundation

struct UsernameValidator {
    static let minLength = 3
    static let maxLength = 30

    private static let validPattern = "^[a-zA-Z0-9_-]+$"
    private static let invalidStartPattern = "^[0-9_-]"

    static func validate(_ username: String) -> ValidationResult {
        guard !username.isEmpty else {
            return .invalid("Логин не может быть пустым")
        }

        guard username.count >= minLength else {
            return .invalid("Минимум \(minLength) символа")
        }

        guard username.count <= maxLength else {
            return .invalid("Максимум \(maxLength) символов")
        }

        if let _ = username.range(of: invalidStartPattern, options: .regularExpression) {
            return .invalid("Нельзя начинать с цифры, - или _")
        }

        if username.lowercased().contains("bot") {
            return .invalid("Нельзя использовать 'bot'")
        }

        guard let _ = username.range(of: validPattern, options: .regularExpression) else {
            return .invalid("Недопустимые символы. Разрешены: a-z, A-Z, 0-9, _, -")
        }

        return .valid
    }

    static func normalize(_ username: String) -> String {
        username.lowercased().trimmingCharacters(in: .whitespaces)
    }
}
```

#### EmailValidator

```swift
// Mac/Barkfluff/Barkfluff/Features/Auth/Validators/EmailValidator.swift

import Foundation

struct EmailValidator {
    static let maxLength = 254

    private static let emailPattern = "[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}"

    static func validate(_ email: String) -> ValidationResult {
        let trimmed = email.trimmingCharacters(in: .whitespaces)

        guard !trimmed.isEmpty else {
            return .invalid("Email не может быть пустым")
        }

        guard trimmed.count <= maxLength else {
            return .invalid("Email слишком длинный")
        }

        guard let _ = trimmed.range(of: emailPattern, options: .regularExpression) else {
            return .invalid("Некорректный формат email")
        }

        return .valid
    }

    static func normalize(_ email: String) -> String {
        email.trimmingCharacters(in: .whitespaces).lowercased()
    }
}
```

#### PasswordValidator

```swift
// Mac/Barkfluff/Barkfluff/Features/Auth/Validators/PasswordValidator.swift

import Foundation

struct PasswordValidator {
    static let minLength = 8
    static let minStrengthScore = 60

    struct PasswordRequirements {
        let hasMinLength: Bool
        let hasUpperCase: Bool
        let hasLowerCase: Bool
        let hasDigit: Bool
        let hasSpecialChar: Bool
        let hasNoSpaces: Bool
        let strengthScore: Int

        var isValid: Bool {
            hasMinLength && hasNoSpaces && strengthScore >= minStrengthScore
        }
    }

    static func validate(_ password: String) -> ValidationResult {
        guard !password.isEmpty else {
            return .invalid("Пароль не может быть пустым")
        }

        guard password.count >= minLength else {
            return .invalid("Пароль должен содержать минимум \(minLength) символов")
        }

        guard !password.contains(" ") else {
            return .invalid("Пароль не должен содержать пробелы")
        }

        let score = calculateStrength(password)
        guard score >= minStrengthScore else {
            return .invalid("Пароль слишком простой. Добавьте буквы разного регистра, цифры и специальные символы")
        }

        return .valid
    }

    static func validateMatch(_ password: String, _ confirmPassword: String) -> ValidationResult {
        guard password == confirmPassword else {
            return .invalid("Пароли не совпадают")
        }
        return .valid
    }

    static func getRequirements(_ password: String) -> PasswordRequirements {
        PasswordRequirements(
            hasMinLength: password.count >= minLength,
            hasUpperCase: password.contains { $0.isUppercase },
            hasLowerCase: password.contains { $0.isLowercase },
            hasDigit: password.contains { $0.isNumber },
            hasSpecialChar: password.contains { !$0.isLetterOrNumber && $0 != " " },
            hasNoSpaces: !password.contains(" "),
            strengthScore: calculateStrength(password)
        )
    }

    static func calculateStrength(_ password: String) -> Int {
        var score = 0

        // Длина
        if password.count >= 8 { score += 20 }
        if password.count >= 12 { score += 10 }
        if password.count >= 16 { score += 10 }

        // Разнообразие символов
        if password.contains(where: { $0.isUppercase }) { score += 15 }
        if password.contains(where: { $0.isLowercase }) { score += 15 }
        if password.contains(where: { $0.isNumber }) { score += 15 }
        if password.contains(where: { !$0.isLetterOrNumber }) { score += 15 }

        return min(100, score)
    }

    static func strengthMessage(_ score: Int) -> (message: String, color: Color) {
        switch score {
        case 0..<30:
            return ("Очень слабый", .red)
        case 30..<60:
            return ("Слабый", .orange)
        case 60..<80:
            return ("Средний", .yellow)
        case 80..<100:
            return ("Хороший", .green)
        default:
            return ("Отличный", .green)
        }
    }
}
```

#### VerificationCodeValidator

```swift
// Mac/Barkfluff/Barkfluff/Features/Auth/Validators/VerificationCodeValidator.swift

import Foundation

struct VerificationCodeValidator {
    static let codeLength = 6

    static func validate(_ code: String) -> ValidationResult {
        let trimmed = code.trimmingCharacters(in: .whitespaces)

        guard !trimmed.isEmpty else {
            return .invalid("Введите код подтверждения")
        }

        guard trimmed.count == codeLength else {
            return .invalid("Код должен содержать \(codeLength) цифр")
        }

        guard trimmed.allSatisfy({ $0.isNumber }) else {
            return .invalid("Код должен содержать только цифры")
        }

        return .valid
    }
}

// MARK: - ValidationResult

enum ValidationResult {
    case valid
    case invalid(String)

    var isValid: Bool {
        if case .valid = self { return true }
        return false
    }

    var message: String? {
        if case .invalid(let msg) = self { return msg }
        return nil
    }
}
```

### 3.3. Обновление AuthServiceProtocol

Необходимо добавить методы для полной регистрации:

```swift
// Mac/Barkfluff/Packages/BFCore/Sources/BFCore/Services/Protocols/AuthServiceProtocol.swift

public protocol AuthServiceProtocol: Sendable {
    // Существующие методы...
    func login(login: String, password: String, otpCode: String?) async throws
    func register(firstName: String, lastName: String, username: String, email: String) async throws -> String
    func confirmAccount(codeID: String, code: String) async throws
    func tryRestoreSession() async -> Bool
    func logout() async

    // НОВЫЕ МЕТОДЫ для регистрации:
    /// Установить пароль после подтверждения аккаунта
    func setPassword(_ password: String) async throws

    /// Получить QR-код для настройки 2FA
    func enableOTP() async throws -> OTPSetupInfo

    /// Подтвердить включение 2FA кодом
    func confirmOTP(code: String) async throws
}
```

### 3.4. Обновление IdentityRepository

Добавить реализацию недостающих методов:

```swift
// Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Repositories/IdentityRepository.swift

extension IdentityRepository {

    // MARK: - SetPassword (НОВЫЙ)

    public func setPassword(_ password: String) async throws {
        var request = Barkfluff_Identity_SetPasswordRequest()
        request.password = password
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                _ = try await identityClient.setPassword(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - EnableOTP (НОВЫЙ)

    public func enableOTP() async throws -> OTPSetupInfo {
        var request = Barkfluff_Identity_EnableOtpVerificationRequest()
        request.otpType = .authenticator
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                let response = try await identityClient.enableOtpVerification(req)
                return OTPSetupInfo(
                    secret: response.otpCode,
                    qrCodeURL: response.otpQr
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - ConfirmOTP (НОВЫЙ)

    public func confirmOTP(code: String) async throws {
        var request = Barkfluff_Identity_ConfirmOtpVerificationRequest()
        request.otpCode = code
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                _ = try await identityClient.confirmOtpVerification(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }
}
```

### 3.5. Обновление AuthService

```swift
// Mac/Barkfluff/Packages/BFCore/Sources/BFCore/Services/Implementations/AuthService.swift

extension AuthService {

    public func setPassword(_ password: String) async throws {
        do {
            try await identityRepository.setPassword(password)
        } catch let error as BFNetworkingError {
            throw Self.mapError(error)
        }
    }

    public func enableOTP() async throws -> OTPSetupInfo {
        do {
            return try await identityRepository.enableOTP()
        } catch let error as BFNetworkingError {
            throw Self.mapError(error)
        }
    }

    public func confirmOTP(code: String) async throws {
        do {
            try await identityRepository.confirmOTP(code: code)
        } catch let error as BFNetworkingError {
            throw Self.mapError(error)
        }
    }
}
```

---

## 4. UI Компоненты

### 4.1. ProgressIndicatorView

```swift
// Mac/Barkfluff/Barkfluff/Features/Auth/Views/Components/ProgressIndicatorView.swift

import SwiftUI

struct ProgressIndicatorView: View {
    let currentStep: Int
    let totalSteps: Int

    var body: some View {
        VStack(spacing: 8) {
            Text("Шаг \(currentStep) из \(totalSteps)")
                .font(.subheadline)
                .foregroundColor(.secondary)

            ProgressView(value: Double(currentStep), total: Double(totalSteps))
                .progressViewStyle(.linear)
                .frame(width: 200)
        }
    }
}
```

### 4.2. OTPInputView

```swift
// Mac/Barkfluff/Barkfluff/Features/Auth/Views/Components/OTPInputView.swift

import SwiftUI

struct OTPInputView: View {
    @Binding var code: String
    let codeLength: Int = 6
    @FocusState private var focusedIndex: Int?

    var body: some View {
        HStack(spacing: 8) {
            ForEach(0..<codeLength, id: \.self) { index in
                OTPDigitBox(
                    digit: digitAt(index),
                    isFocused: focusedIndex == index
                )
                .onTapGesture {
                    focusedIndex = index
                }
            }
        }
        .onAppear {
            focusedIndex = 0
        }
    }

    private func digitAt(_ index: Int) -> String {
        guard index < code.count else { return "" }
        let index = code.index(code.startIndex, offsetBy: index)
        return String(code[index])
    }
}

struct OTPDigitBox: View {
    let digit: String
    let isFocused: Bool

    var body: some View {
        Text(digit)
            .font(.title2)
            .fontWeight(.semibold)
            .frame(width: 44, height: 52)
            .background(Color(NSColor.controlBackgroundColor))
            .cornerRadius(8)
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .stroke(isFocused ? Color.accentColor : Color.clear, lineWidth: 2)
            )
    }
}
```

### 4.3. PasswordStrengthView

```swift
// Mac/Barkfluff/Barkfluff/Features/Auth/Views/Components/PasswordStrengthView.swift

import SwiftUI

struct PasswordStrengthView: View {
    let requirements: PasswordValidator.PasswordRequirements

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            // Strength bar
            GeometryReader { geometry in
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 2)
                        .fill(Color.gray.opacity(0.3))
                        .frame(height: 4)

                    RoundedRectangle(cornerRadius: 2)
                        .fill(strengthColor)
                        .frame(width: geometry.size.width * CGFloat(requirements.strengthScore) / 100, height: 4)
                }
            }
            .frame(height: 4)

            // Requirements checklist
            VStack(alignment: .leading, spacing: 4) {
                RequirementRow(isMet: requirements.hasMinLength, text: "Минимум 8 символов")
                RequirementRow(isMet: requirements.hasUpperCase, text: "Заглавные буквы")
                RequirementRow(isMet: requirements.hasLowerCase, text: "Строчные буквы")
                RequirementRow(isMet: requirements.hasDigit, text: "Цифры")
                RequirementRow(isMet: requirements.hasSpecialChar, text: "Специальные символы")
            }
            .font(.caption)
        }
    }

    private var strengthColor: Color {
        let (message, color) = PasswordValidator.strengthMessage(requirements.strengthScore)
        return color
    }
}

struct RequirementRow: View {
    let isMet: Bool
    let text: String

    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: isMet ? "checkmark.circle.fill" : "circle")
                .foregroundColor(isMet ? .green : .secondary)
            Text(text)
                .foregroundColor(isMet ? .primary : .secondary)
        }
    }
}
```

---

## 5. Обновлённый RegisterViewModel

```swift
// Mac/Barkfluff/Barkfluff/Features/Auth/ViewModels/RegisterViewModel.swift

import SwiftUI
import Observation
import BFCore

@Observable
final class RegisterViewModel {
    // MARK: - State
    let state: RegistrationStateService
    private let authService: AuthServiceProtocol
    private let userService: UserServiceProtocol
    private let fileService: FileServiceProtocol
    private let coordinator: AppCoordinator

    // MARK: - Computed Properties
    var currentStep: RegistrationStep { state.currentStep }
    var isLoading: Bool { state.isLoading }
    var errorMessage: String? { state.errorMessage }

    // MARK: - Init
    init(
        authService: AuthServiceProtocol,
        userService: UserServiceProtocol,
        fileService: FileServiceProtocol,
        coordinator: AppCoordinator
    ) {
        self.state = RegistrationStateService()
        self.authService = authService
        self.userService = userService
        self.fileService = fileService
        self.coordinator = coordinator
    }

    // MARK: - Navigation
    func goToNextStep() {
        if let next = state.currentStep.next {
            state.currentStep = next
        }
    }

    func goToPreviousStep() {
        if let previous = state.currentStep.previous {
            state.currentStep = previous
        }
    }

    // MARK: - Step Processing

    /// Step 1: Personal Info
    func validatePersonalInfo() -> Bool {
        let firstNameResult = PersonalInfoValidator.validateFirstName(state.firstName)
        let lastNameResult = PersonalInfoValidator.validateLastName(state.lastName)

        if let error = firstNameResult.message ?? lastNameResult.message {
            state.errorMessage = error
            return false
        }

        state.errorMessage = nil
        return true
    }

    /// Step 2: Username (с проверкой на сервере)
    func validateAndCheckUsername() async -> Bool {
        // Локальная валидация
        let result = UsernameValidator.validate(state.username)
        guard result.isValid else {
            state.errorMessage = result.message
            return false
        }

        // Проверка на сервере
        state.isLoading = true
        defer { state.isLoading = false }

        do {
            let exists = try await userService.checkUsernameExists(state.username)
            if exists {
                state.errorMessage = "Имя пользователя уже занято"
                return false
            }
            state.errorMessage = nil
            return true
        } catch {
            state.errorMessage = "Ошибка подключения к серверу"
            return false
        }
    }

    /// Step 3: Email (вызов CreateAccount API)
    func validateEmailAndCreateAccount() async -> Bool {
        let result = EmailValidator.validate(state.email)
        guard result.isValid else {
            state.errorMessage = result.message
            return false
        }

        state.isLoading = true
        defer { state.isLoading = false }

        do {
            state.email = EmailValidator.normalize(state.email)
            state.username = UsernameValidator.normalize(state.username)

            let codeID = try await authService.register(
                firstName: state.firstName,
                lastName: state.lastName,
                username: state.username,
                email: state.email
            )
            state.codeID = codeID
            state.errorMessage = nil
            return true
        } catch {
            state.errorMessage = error.localizedDescription
            return false
        }
    }

    /// Step 4: Confirm Email
    func confirmEmail(code: String) async -> Bool {
        let result = VerificationCodeValidator.validate(code)
        guard result.isValid else {
            state.errorMessage = result.message
            return false
        }

        // Rate limiting check
        guard state.canAttemptVerification else {
            if let remaining = state.cooldownRemaining {
                state.errorMessage = "Подождите \(Int(remaining / 60)) мин. перед следующей попыткой"
            }
            return false
        }

        state.isLoading = true
        defer { state.isLoading = false }

        do {
            try await authService.confirmAccount(codeID: state.codeID, code: code)
            state.recordVerificationAttempt(success: true)
            state.errorMessage = nil
            return true
        } catch {
            state.recordVerificationAttempt(success: false)
            state.errorMessage = error.localizedDescription
            return false
        }
    }

    /// Step 5: Set Password (КРИТИЧНО!)
    func setPassword(_ password: String, confirmPassword: String) async -> Bool {
        // Валидация
        let result = PasswordValidator.validate(password)
        guard result.isValid else {
            state.errorMessage = result.message
            return false
        }

        let matchResult = PasswordValidator.validateMatch(password, confirmPassword)
        guard matchResult.isValid else {
            state.errorMessage = matchResult.message
            return false
        }

        state.isLoading = true
        defer { state.isLoading = false }

        do {
            try await authService.setPassword(password)
            state.password = "" // Очищаем после использования
            state.errorMessage = nil
            return true
        } catch {
            state.errorMessage = error.localizedDescription
            return false
        }
    }

    /// Step 6: Upload Avatar
    func uploadAvatar(imageData: Data) async -> Bool {
        state.isLoading = true
        defer { state.isLoading = false }

        do {
            // 1. Загружаем файл
            let fileID = try await fileService.uploadFile(
                data: imageData,
                fileName: "avatar.jpg",
                fileType: .userAvatar
            )

            // 2. Устанавливаем как аватар
            try await userService.setProfilePicture(fileID: fileID)

            state.avatarImageData = imageData
            state.errorMessage = nil
            return true
        } catch {
            state.errorMessage = error.localizedDescription
            return false
        }
    }

    /// Step 7: Set Bio
    func setBio(_ bio: String) async -> Bool {
        state.isLoading = true
        defer { state.isLoading = false }

        do {
            if !bio.isEmpty {
                try await userService.changeBio(newBio: bio)
            }
            state.bio = bio
            state.errorMessage = nil
            return true
        } catch {
            state.errorMessage = error.localizedDescription
            return false
        }
    }

    /// Step 8: Setup 2FA
    var otpSetupInfo: OTPSetupInfo?

    func enableOTP() async -> Bool {
        state.isLoading = true
        defer { state.isLoading = false }

        do {
            otpSetupInfo = try await authService.enableOTP()
            state.errorMessage = nil
            return true
        } catch {
            state.errorMessage = error.localizedDescription
            return false
        }
    }

    func confirmOTP(code: String) async -> Bool {
        let result = VerificationCodeValidator.validate(code)
        guard result.isValid else {
            state.errorMessage = result.message
            return false
        }

        state.isLoading = true
        defer { state.isLoading = false }

        do {
            try await authService.confirmOTP(code: code)
            state.errorMessage = nil
            return true
        } catch {
            state.errorMessage = error.localizedDescription
            return false
        }
    }

    /// Step 9: Complete Registration
    func completeRegistration() {
        coordinator.currentState = .main
    }

    /// Skip optional steps
    func skipCurrentStep() {
        switch state.currentStep {
        case .avatar, .bio, .twoFA:
            goToNextStep()
        default:
            break
        }
    }
}
```

---

## 6. Обновлённый RegisterView

```swift
// Mac/Barkfluff/Barkfluff/Features/Auth/Views/RegisterView.swift

import SwiftUI
import BFCore

struct RegisterView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: RegisterViewModel?

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            if let viewModel {
                headerView(viewModel)
                progressView(viewModel)

                ScrollView {
                    stepContentView(viewModel)
                }

                navigationButtons(viewModel)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear {
            if viewModel == nil {
                viewModel = RegisterViewModel(
                    authService: container.authService,
                    userService: container.userService,
                    fileService: container.fileService,
                    coordinator: coordinator
                )
            }
        }
    }

    // MARK: - Header
    @ViewBuilder
    private func headerView(_ viewModel: RegisterViewModel) -> some View {
        VStack(spacing: 8) {
            HStack(spacing: 12) {
                Image("barkfluff_logo")
                    .resizable()
                    .frame(width: 28, height: 28)

                Text("BarkFluff")
                    .font(.title2)
                    .fontWeight(.semibold)
            }

            Text(viewModel.state.currentStep.title)
                .font(.title3)
                .foregroundColor(.secondary)
        }
        .padding(.top, 20)
    }

    // MARK: - Progress
    @ViewBuilder
    private func progressView(_ viewModel: RegisterViewModel) -> some View {
        ProgressIndicatorView(
            currentStep: viewModel.currentStep.rawValue,
            totalSteps: RegistrationStep.totalSteps
        )
        .padding(.horizontal, 40)
    }

    // MARK: - Step Content
    @ViewBuilder
    private func stepContentView(_ viewModel: RegisterViewModel) -> some View {
        switch viewModel.currentStep {
        case .personalInfo:
            Step1_PersonalInfoView(viewModel: viewModel)
        case .username:
            Step2_UsernameView(viewModel: viewModel)
        case .email:
            Step3_EmailView(viewModel: viewModel)
        case .confirmEmail:
            Step4_ConfirmEmailView(viewModel: viewModel)
        case .password:
            Step5_PasswordView(viewModel: viewModel)
        case .avatar:
            Step6_AvatarView(viewModel: viewModel)
        case .bio:
            Step7_BioView(viewModel: viewModel)
        case .twoFA:
            Step8_TwoFAView(viewModel: viewModel)
        case .completion:
            Step9_CompletionView(viewModel: viewModel)
        }
    }

    // MARK: - Navigation Buttons
    @ViewBuilder
    private func navigationButtons(_ viewModel: RegisterViewModel) -> some View {
        HStack(spacing: 16) {
            if viewModel.currentStep != .personalInfo {
                Button("Назад") {
                    viewModel.goToPreviousStep()
                }
                .buttonStyle(.bordered)
            }

            Button(viewModel.currentStep == .completion ? "Готово" : "Далее") {
                Task { await processCurrentStep(viewModel) }
            }
            .buttonStyle(.borderedProminent)
            .disabled(viewModel.isLoading)

            if viewModel.currentStep == .avatar || viewModel.currentStep == .twoFA {
                Button("Пропустить") {
                    viewModel.skipCurrentStep()
                }
                .buttonStyle(.plain)
                .foregroundColor(.secondary)
            }
        }
        .padding(.bottom, 20)
    }

    private func processCurrentStep(_ viewModel: RegisterViewModel) async {
        // Обработка зависит от текущего шага
        // Реализуется в соответствующих View
    }
}
```

---

## 7. План реализации

### Фаза 1: Критические исправления (Приоритет 1)

1. **Добавить PasswordValidator** — скопировать из WPF
2. **Добавить шаг установки пароля** — без этого регистрация бесполезна
3. **Реализовать setPassword в IdentityRepository** — вызвать gRPC API
4. **Обновить RegisterViewModel** — добавить обработку шага пароля

**Оценка:** 2-4 часа

### Фаза 2: Валидация (Приоритет 1)

1. Создать все валидаторы
2. Добавить проверку username на сервере
3. Добавить rate limiting для кодов подтверждения
4. Показывать ошибки валидации в UI

**Оценка:** 4-6 часов

### Фаза 3: Дополнительные шаги (Приоритет 2)

1. **Аватар** — FilePicker + Crop + Upload
2. **Bio** — TextView + API call
3. **2FA** — QR-код + код

**Оценка:** 6-8 часов

### Фаза 4: UI улучшения (Приоритет 3)

1. Разделение на отдельные экраны
2. Анимации переходов
3. Индикатор прогресса
4. Стилистика как в WPF

**Оценка:** 4-6 часов

---

## 8. API Endpoints (справочник)

### Identity API (identity_api.proto)

| Метод | Описание | Статус |
|-------|----------|--------|
| `CreateAccount` | Создать черновик аккаунта, получить codeID | ✅ Реализован |
| `ConfirmAccount` | Подтвердить email, получить refresh token | ✅ Реализован |
| `SetPassword` | Установить пароль | ❌ **Нужна реализация!** |
| `EnableOtpVerification` | Получить QR для 2FA | ❌ Нужна реализация |
| `ConfirmOtpVerification` | Подтвердить 2FA кодом | ❌ Нужна реализация |

### Users API (users_api.proto)

| Метод | Описание | Статус |
|-------|----------|--------|
| `CheckExistUsername` | Проверить занятость username | ❌ Нужна реализация |
| `SetProfilePicture` | Установить аватар | ✅ Реализован |
| `ChangeBio` | Изменить описание | ✅ Реализован |
| `GetUser` | Получить данные пользователя | ✅ Реализован |

### Files API (files_api.proto)

| Метод | Описание | Статус |
|-------|----------|--------|
| `GetUploadUrl` | Получить URL для загрузки | ✅ Реализован |
| `UploadFile` | Загрузить файл (HTTP POST) | ✅ Реализован |
| `CheckFileHash` | Проверить дубликат по хешу | ✅ Реализован |

---

## 9. Тестирование

### Критические сценарии для проверки

1. **Регистрация без пароля** → должна требовать пароль на шаге 5
2. **Слабый пароль** → должна показывать ошибку
3. **Занятый username** → должна проверять на сервере
4. **Неверный код подтверждения** → rate limiting
5. **Превышение попыток ввода кода** → кулдаун 15 минут
6. **Загрузка аватара** → проверить типы файлов, размеры
7. **2FA подключение** → проверить QR-код и валидацию кода

---

## 10. Заключение

### Минимально необходимые доработки для работоспособности:

1. **PasswordValidator.swift** — валидация пароля
2. **Шаг 5 (Password)** в RegisterViewModel
3. **IdentityRepository.setPassword()** — вызов gRPC

### Полный флоу регистрации требует:

- 5 валидаторов
- 7 новых View компонентов
- 3 новых метода в репозиториях
- Обновление AuthService и UserService
- Rate limiting для кодов подтверждения
- UI для 9 шагов регистрации

---

**Автор:** Claude Code
**Дата:** 2026-02-13
**Версия:** 1.0
