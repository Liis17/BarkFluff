# 🌐 Автоматический перевод сообщений

> Категория: UX / Доступность
> Платформы: ВСЕ
> Приоритет: 🟡 Средний
> Сложность: ⭐⭐⭐

---

## Описание

Кнопка **«Перевести»** под сообщением на иностранном языке, или режим **«Автоперевод чата»** — все входящие сообщения автоматически переводятся на язык пользователя прямо в пузыре.

---

## Ключевые возможности

- «Перевести» по tap/click на конкретном сообщении
- Режим «Автоперевод» для всего чата (переключатель в настройках чата)
- Показывать оригинал / перевод (переключение прямо в пузыре)
- Определение языка автоматически
- Выбор целевого языка в профиле (по умолчанию = язык системы)
- Перевод не отправляется собеседнику — только локально на устройстве

---

## Архитектура — 2 варианта

### Вариант A: Клиентский (On-device, бесплатно)

| Платформа | Решение |
|-----------|---------|
| **Android** | ML Kit Translation API — `com.google.mlkit:translate` (скачивает языковые модели on-device) |
| **iOS/macOS** | `Translation` framework (iOS 17.4+ / macOS 14.4+) — нативный Apple API |
| **WPF** | ML.NET или вызов бесплатного API (LibreTranslate self-hosted) |

### Вариант B: Серверный (централизовано)

```
Новый микросервис: BarkFluff.Translator (порт 7070)
     │
     ├── Self-hosted LibreTranslate (Docker, бесплатный open-source)
     │   или DeepL Free API (500k символов/мес)
     └── Redis кеш переводов (ключ: hash(text+lang) → перевод, TTL 7 дней)
```

```protobuf
rpc TranslateMessage(TranslateRequest) returns (TranslateResponse);

message TranslateRequest {
  string text = 1;
  string target_language = 2;   // "ru", "en", "de", ...
  string source_language = 3;   // "" = auto-detect
}
```

**Рекомендуется Вариант A** для Android и macOS/iOS (приватно, оффлайн), Вариант B — для WPF.

---

## Клиентские особенности

### Android — ML Kit

```kotlin
// Определение языка
val languageIdentifier = LanguageIdentification.getClient()
languageIdentifier.identifyLanguage(text).addOnSuccessListener { lang ->
    // Скачать модель если нет → перевести
    val options = TranslatorOptions.Builder()
        .setSourceLanguage(TranslateLanguage.forLanguageTag(lang)!!)
        .setTargetLanguage(TranslateLanguage.RUSSIAN)
        .build()
    val translator = Translation.getClient(options)
    translator.downloadModelIfNeeded().addOnSuccessListener {
        translator.translate(text)...
    }
}
```

### macOS / iOS (iOS 17.4+)

```swift
import Translation

// Нативный системный UI перевода одной строкой:
Text(message.text)
    .translationPresentation(isPresented: $showTranslation, text: message.text)
```

---

## UI

- Кнопка «Перевести» появляется при long-press → меню сообщения
- Перевод отображается курсивом серым цветом под оригинальным текстом
- Ссылка «Показать оригинал» / «Показать перевод» переключает вид
- Флаг языка оригинала 🇩🇪 рядом с переводом

