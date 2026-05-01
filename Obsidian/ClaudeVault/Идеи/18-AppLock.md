# 🔒 PIN-блокировка и биометрия на Android / iOS

> Категория: Безопасность
> Платформы: **Android**, **iOS**, **macOS** (WPF уже имеет PIN)
> Приоритет: 🟢 Простая реализация
> Сложность: ⭐⭐

---

## Описание

Блокировка приложения через **PIN-код** и/или **биометрию** (отпечаток, Face ID) при сворачивании. Аналог уже реализованного в [[../Клиенты/Windows-WPF]] — `PinCodePage` с шифрованием GlobalParam через PBKDF2. Нужно перенести идею на мобильные и macOS клиенты.

---

## Ключевые возможности

- Включить PIN/биометрию в настройках
- Запрос PIN/биометрии при:
  - Возврате в приложение после N минут фона (настраивается: 1 / 5 / 30 мин / сразу)
  - Холодном старте
- 5 неверных попыток → экстренный выход (опционально)
- Биометрия как альтернатива PIN
- «Скрытый режим» при блокировке — не показывает превью сообщений в Recent Apps (флаг `FLAG_SECURE`)

---

## Android

```kotlin
// BiometricPrompt API (androidx.biometric:biometric)
val biometricPrompt = BiometricPrompt(activity, executor,
    object : BiometricPrompt.AuthenticationCallback() {
        override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) {
            unlockApp()
        }
    })

val promptInfo = BiometricPrompt.PromptInfo.Builder()
    .setTitle("BarkFluff")
    .setSubtitle("Подтвердите личность")
    .setNegativeButtonText("Использовать PIN")
    .build()
biometricPrompt.authenticate(promptInfo)
```

- `LockManager.kt` — синглтон, отслеживает `onPause` / `onResume` через `ProcessLifecycleOwner`
- PIN хранится как bcrypt-хеш в `EncryptedSharedPreferences`
- `window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)` — защита от скриншотов при заблокированном экране
- Новый `LockActivity.kt` — экран ввода PIN (поверх всей навигации через `FLAG_ACTIVITY_NEW_TASK`)

---

## iOS / macOS (Swift)

```swift
import LocalAuthentication

// LAContext для Face ID / Touch ID
let context = LAContext()
var error: NSError?

if context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error) {
    context.evaluatePolicy(.deviceOwnerAuthenticationWithBiometrics,
                           localizedReason: "Доступ к BarkFluff") { success, _ in
        DispatchQueue.main.async {
            if success { self.unlockApp() }
        }
    }
}
```

- `AppLockCoordinator.swift` — `@Observable`, встраивается в `AppCoordinator`
- `.privacySensitive()` на окне → система скрывает контент в switcher
- PIN хранится в Keychain (`KeychainAccess` уже есть как зависимость)

---

## Состояния приложения

```
Разблокировано → (фон > N мин) → Заблокировано → (PIN / биометрия OK) → Разблокировано
                                                → (5 ошибок) → Выход
```

---

## Настройки

| Опция | Тип |
|-------|-----|
| Включить блокировку | Toggle |
| Время до блокировки | Picker (сразу / 1 мин / 5 мин / 30 мин) |
| Использовать биометрию | Toggle |
| Скрыть содержимое в switcher | Toggle |

