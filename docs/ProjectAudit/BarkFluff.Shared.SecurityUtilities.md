# Аудит: BarkFluff.Shared.SecurityUtilities

> **Дата создания:** 2025-07  
> **Последняя проверка актуальности:** 2026-05-18  
> **Проект:** `Shared/BarkFluff.Shared.SecurityUtilities/`  
> **Target Framework:** net10.0  
> **Файлов в проекте:** 1 (`SecurityUtilities.cs`)  
> **Используется в:** `BarkFluff.Client.WPF` (WPF), `Linux` (Qt/C++ — собственная реализация в `Linux/src/Utils/Validators.cpp` с расходящимся алгоритмом)

## Сводка по статусу актуальности (2026-05-18)

- 🔄 **Частично исправлено:** OPT-02 — паттерн со `static readonly BrushConverter` уже применён в `PasswordReset.xaml.cs:34, 432`, но в `CreateAccount.xaml.cs:204` `new BrushConverter()` создаётся на каждый keystroke по-прежнему.
- ⚠️ **Остаётся:** SEC-03, SEC-04, OPT-01, OPT-03, BUG-01, BUG-02, BUG-03, QA-01, QA-02, QA-03 (проект `*.Tests` не обнаружен).
- ⚠️ **Дополнительно:** Linux-реализация в `Validators.cpp` использует другой алгоритм оценки (другие весовые коэффициенты по длине), что создаёт расхождение в score между WPF и Linux-клиентом — это потенциальный отдельный пункт для следующей итерации аудита.

В сводной таблице упоминаются SEC-01 и SEC-02, описания которых отсутствуют в теле документа — они должны быть восстановлены или удалены в следующей ревизии.

---

## Содержание



## 🔴 Безопасность

---

### ---

### SEC-03 — Порог MinStrengthScore достижим при коротком пароле

**Проблема / Описание**  
`MinStrengthScore = 60` задан в `PasswordValidator.cs` (WPF), но при этом минимальная длина проверяется отдельно до вызова `EvaluatePasswordStrength`. Проблема в том, что оценщик сам по себе не блокирует пароли короче 8 символов — он просто возвращает низкий score. Если кто-то использует `EvaluatePasswordStrength` напрямую без `Validate()`, минимум длины не применяется.

**Конкретно в чём проблема**  
Класс `SecurityUtilities` — публичный shared. Любой потребитель (например, будущий backend-сервис) может вызвать `EvaluatePasswordStrength` напрямую и не знать, что нужна отдельная проверка длины.

**Путь к файлу:**  
`Shared/BarkFluff.Shared.SecurityUtilities/SecurityUtilities.cs` : 5–47  
`Windows/BarkFluff.Client.WPF/Validators/PasswordValidator.cs` : 14–44

```csharp
// SecurityUtilities.cs — нет минимальной длины как hard-limit:
if (string.IsNullOrEmpty(password))
    return 0;
// Пароль из 3 символов "A1!" дойдёт до вычисления score

// PasswordValidator.cs — проверка длины ОТДЕЛЬНО от SecurityUtilities:
if (password.Length < MinLength)          // ← это знает только WPF Validator
{
    errorMessage = $"Пароль должен содержать минимум {MinLength} символов";
    return false;
}
var strength = SecurityUtilities.EvaluatePasswordStrength(password); // ← вызывается после
```

**Варианты решения**

Добавить константу минимальной длины прямо в `SecurityUtilities` и вернуть 0 если пароль короче.

```csharp
public class SecurityUtilities
{
    /// <summary>Минимальная длина пароля, при которой score > 0</summary>
    public const int MinPasswordLength = 8;

    public static int EvaluatePasswordStrength(string password)
    {
        // Явный hard-limit прямо в оценщике
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return 0;

        int score = 0;
        // ... остальная логика без изменений
    }
}
```

---

### SEC-04 — Дублирующая логика требований в PasswordValidator.cs

**Проблема / Описание**  
`GetRequirementsStatus()` в `PasswordValidator.cs` повторяет часть логики из `EvaluatePasswordStrength` — самостоятельно проверяет наличие букв разного регистра, цифр, спецсимволов. При изменении правил в `SecurityUtilities` `PasswordValidator` не обновится автоматически.

**Конкретно в чём проблема**  
Логика "что считается спецсимволом" расходится: `SecurityUtilities` считает `!char.IsLetterOrDigit(c)` (включая пробел), а `PasswordValidator` использует `!char.IsLetterOrDigit(c) && c != ' '` — то есть пробел в одном месте спецсимвол, в другом нет.

**Путь к файлу:**  
`Shared/BarkFluff.Shared.SecurityUtilities/SecurityUtilities.cs` : 34  
`Windows/BarkFluff.Client.WPF/Validators/PasswordValidator.cs` : 85

```csharp
// SecurityUtilities.cs — пробел СЧИТАЕТСЯ спецсимволом:
int specialCount = password.Count(c => !char.IsLetterOrDigit(c)); // ← ' ' попадает сюда

// PasswordValidator.cs — пробел НЕ СЧИТАЕТСЯ спецсимволом:
HasSpecialChar: password.Any(c => !char.IsLetterOrDigit(c) && c != ' '), // ← ' ' исключён
```

**Варианты решения**

Перенести детальный разбор требований в `SecurityUtilities` и возвращать структуру с деталями.

```csharp
// В SecurityUtilities.cs — единый источник правил
public record PasswordAnalysis(
    bool HasMinLength,
    bool HasUpperCase,
    bool HasLowerCase,
    bool HasDigit,
    bool HasSpecialChar,  // пробел НЕ считается спецсимволом
    bool HasNoSpaces,
    int Score
);

public static PasswordAnalysis Analyze(string? password)
{
    if (string.IsNullOrEmpty(password))
        return new PasswordAnalysis(false, false, false, false, false, true, 0);

    return new PasswordAnalysis(
        HasMinLength:    password.Length >= MinPasswordLength,
        HasUpperCase:    password.Any(char.IsUpper),
        HasLowerCase:    password.Any(char.IsLower),
        HasDigit:        password.Any(char.IsDigit),
        HasSpecialChar:  password.Any(c => !char.IsLetterOrDigit(c) && c != ' '), // единая логика
        HasNoSpaces:     !password.Contains(' '),
        Score:           EvaluatePasswordStrength(password)
    );
}
```

---

## 🟡 Оптимизация

---

### OPT-01 — Множественные проходы по строке в EvaluatePasswordStrength

**Проблема / Описание**  
Метод `EvaluatePasswordStrength` выполняет **6 отдельных проходов** по строке: `Any(IsLower)`, `Any(IsUpper)`, `Count(IsDigit)`, `Count(!IsLetterOrDigit)`, `Distinct().Count()` и неявный `.Length`. При вызове на каждый keystroke (UI-событие) это 6× итераций по каждому символу.

**Конкретно в чём проблема**  
Для пароля длиной N символов выполняется O(6N) операций, хотя достаточно O(N) с одним проходом.

**Путь к файлу:**  
`Shared/BarkFluff.Shared.SecurityUtilities/SecurityUtilities.cs` : 21–44

```csharp
bool hasLower = password.Any(char.IsLower);         // проход 1
bool hasUpper = password.Any(char.IsUpper);         // проход 2
int digitCount = password.Count(char.IsDigit);      // проход 3
int specialCount = password.Count(c => !char.IsLetterOrDigit(c)); // проход 4
double uniquenessRatio = password.Distinct().Count() // проход 5 (Distinct + Count)
    / (double)password.Length;
```

**Варианты решения**

Один проход с накоплением всех метрик через `foreach`.

```csharp
public static int EvaluatePasswordStrength(string password)
{
    if (string.IsNullOrEmpty(password)) return 0;

    // Один проход по строке — собираем всё сразу
    bool hasLower = false, hasUpper = false;
    int digitCount = 0, specialCount = 0;
    var seen = new HashSet<char>();

    foreach (char c in password)
    {
        seen.Add(c);
        if (char.IsLower(c))              hasLower = true;
        else if (char.IsUpper(c))         hasUpper = true;
        else if (char.IsDigit(c))         digitCount++;
        else if (c != ' ')                specialCount++; // заодно исправляем SEC-04
    }

    int score = 0;

    // Length
    if (password.Length >= 16)      score += 30;
    else if (password.Length >= 12) score += 20;
    else if (password.Length >= 8)  score += 10;

    // Case
    if (hasLower && hasUpper) score += 20;
    else if (hasLower || hasUpper) score += 10;

    // Digits
    if (digitCount >= 3)      score += 20;
    else if (digitCount > 0)  score += 10;

    // Specials
    if (specialCount >= 2)      score += 20;
    else if (specialCount == 1) score += 10;

    // Uniqueness
    double uniquenessRatio = seen.Count / (double)password.Length;
    if (uniquenessRatio > 0.7)      score += 10;
    else if (uniquenessRatio > 0.4) score += 5;

    return Math.Min(score, 100);
}
```

---

### OPT-02 — BrushConverter создаётся на каждый вызов в CreateAccount 🔄 ЧАСТИЧНО ИСПРАВЛЕНО (2026-05-18)

> **Статус 2026-05-18:** В `PasswordReset.xaml.cs:34, 432` уже введён `static readonly BrushConverter BrushConverterInstance = new();` и используется в обработчике. В `CreateAccount.xaml.cs:204` всё ещё создаётся `new BrushConverter()` на каждый keystroke — надо перенести тот же паттерн.

**Проблема / Описание**  
В `CreateAccount.xaml.cs` при каждом нажатии клавиши создаётся новый `BrushConverter` через `new BrushConverter()`. `BrushConverter` — тяжёлый объект WPF-конвертации.

**Конкретно в чём проблема**  
При быстром вводе пароля создаются десятки экземпляров `BrushConverter` и сразу становятся мусором.

**Путь к файлу:**  
`Windows/BarkFluff.Client.WPF/Pages/SetupPages/CreateAccount.xaml.cs` : 204

```csharp
// Каждое нажатие клавиши → new BrushConverter()
PasswordStrengthBar.Foreground = (Brush)new BrushConverter().ConvertFromString(colors.colorHex)!;
//                                       ^^^^^^^^^^^^^^^^^^^ создаётся заново каждый раз
```

**Варианты решения**

Кешировать конвертер как static readonly или заменить на словарь готовых кистей.

```csharp
// Вариант А — static readonly конвертер (как уже сделано в PasswordReset.xaml.cs!)
private static readonly BrushConverter BrushConverterInstance = new();

// В обработчике:
PasswordStrengthBar.Foreground = (Brush)BrushConverterInstance.ConvertFromString(colors.colorHex)!;

// Вариант Б — заранее созданные кисти для 5 фиксированных цветов
private static readonly Dictionary<string, SolidColorBrush> StrengthBrushes = new()
{
    ["#FF4C4C"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x4C, 0x4C)),
    ["#FF8000"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x00)),
    ["#FFD700"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
    ["#7FFF00"] = new SolidColorBrush(Color.FromRgb(0x7F, 0xFF, 0x00)),
    ["#00CC66"] = new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0x66)),
};
// Все кисти заморожены → thread-safe и без аллокаций
```

---

### OPT-03 — EvaluatePasswordStrength вызывается дважды на одно событие

**Проблема / Описание**  
В `CreateAccount.xaml.cs` и `PasswordReset.xaml.cs` при каждом изменении пароля `EvaluatePasswordStrength` вызывается **дважды**: один раз напрямую для UI-бара, и второй раз внутри `PasswordValidator.GetRequirementsStatus()`.

**Конкретно в чём проблема**  
Двойная работа на каждый keystroke без какой-либо пользы.

**Путь к файлу:**  
`Windows/BarkFluff.Client.WPF/Pages/SetupPages/CreateAccount.xaml.cs` : 200–207  
`Windows/BarkFluff.Client.WPF/Pages/PasswordReset.xaml.cs` : 425–437

```csharp
// CreateAccount.xaml.cs — вызов 1:
var strength = SecurityUtilities.EvaluatePasswordStrength(password); // ← вычисляем score
PasswordStrengthBar.Value = strength;

// ...несколько строк спустя...

// Вызов 2 — ВНУТРИ GetRequirementsStatus тоже вызывается EvaluatePasswordStrength:
var requirements = PasswordValidator.GetRequirementsStatus(password);
// PasswordValidator.cs:87 → StrengthScore: SecurityUtilities.EvaluatePasswordStrength(password)
```

**Варианты решения**

Использовать `GetRequirementsStatus` как единственный источник, брать `StrengthScore` из него же.

```csharp
// Один вызов вместо двух:
var requirements = PasswordValidator.GetRequirementsStatus(password);

// Берём score из уже вычисленного результата
PasswordStrengthBar.Value = requirements.StrengthScore;
var (message, colorHex) = SecurityUtilities.GetPasswordStrengthMessage(requirements.StrengthScore);
PasswordDifficultyIndicator.Text = message;
PasswordStrengthBar.Foreground = (Brush)BrushConverterInstance.ConvertFromString(colorHex)!;

// Требования — из того же объекта
UpdateRequirementText(ReqMinLength,   requirements.HasMinLength,   "Минимум 8 символов");
UpdateRequirementText(ReqUpperCase,   requirements.HasUpperCase,   "Заглавные буквы");
// ...
```

---

## 🔵 Баги и недоработки

---

### BUG-01 — Пробел засчитывается как спецсимвол в оценке силы

**Проблема / Описание**  
В `EvaluatePasswordStrength` пробел (`' '`) не исключён из подсчёта спецсимволов — `!char.IsLetterOrDigit(' ')` возвращает `true`. Одновременно `PasswordValidator.Validate()` запрещает пробелы в пароле. Таким образом, при вводе пробела пользователь получает прирост score, но пароль всё равно не пройдёт валидацию.

**Конкретно в чём проблема**  
Пользователь вводит пароль с пробелом, видит что индикатор "стал лучше", но при отправке получает ошибку — путаница в UX.

**Путь к файлу:**  
`Shared/BarkFluff.Shared.SecurityUtilities/SecurityUtilities.cs` : 34  
`Windows/BarkFluff.Client.WPF/Validators/PasswordValidator.cs` : 30–33

```csharp
// SecurityUtilities.cs — пробел учитывается как спецсимвол:
int specialCount = password.Count(c => !char.IsLetterOrDigit(c));
//                                     ^^^^^^^^^^^^^^^^^^^^^^^^^ ' ' → true → score растёт

// PasswordValidator.cs — пробел запрещён:
if (password.Contains(' '))
{
    errorMessage = "Пароль не должен содержать пробелы";
    return false; // ← но score уже был показан выше
}
```

**Варианты решения**

Исключить пробел из подсчёта спецсимволов в `EvaluatePasswordStrength`.

```csharp
// Исправление в SecurityUtilities.cs
int specialCount = password.Count(c => !char.IsLetterOrDigit(c) && c != ' ');
//                                                               ^^^^^^^^^^^^ пробел не спецсимвол
```

---

### BUG-02 — ValidationState.InvalidCharacters используется для слабого пароля

**Проблема / Описание**  
В `ValidateRealTime()` когда пароль слишком слабый по score, возвращается состояние `ValidationState.InvalidCharacters` — что семантически некорректно. Слабый пароль — это не "недопустимые символы".

**Конкретно в чём проблема**  
Любой код, который принимает решения на основе `ValidationState` (анимации, иконки, логи), получит неверную категорию ошибки.

**Путь к файлу:**  
`Windows/BarkFluff.Client.WPF/Validators/PasswordValidator.cs` : 111–115

```csharp
var strength = SecurityUtilities.EvaluatePasswordStrength(password);
if (strength < MinStrengthScore)
{
    // BUG: InvalidCharacters — неверный ValidationState для слабого пароля
    return new ValidationResult(false, "Пароль слишком простой", ValidationState.InvalidCharacters);
}
```

**Варианты решения**

Добавить специализированное состояние или использовать существующее подходящее.

```csharp
// Вариант — добавить TooWeak в перечисление ValidationState:
// public enum ValidationState { Empty, TooShort, TooWeak, InvalidCharacters, Valid }

return new ValidationResult(false, "Пароль слишком простой", ValidationState.TooWeak); // ← семантически верно
```

---

### BUG-03 — Отсутствует проверка на повторяющиеся паттерны

**Проблема / Описание**  
Алгоритм начисляет бонус за уникальность символов (`uniquenessRatio`), но не штрафует за очевидные паттерны: клавиатурные последовательности (`qwerty`, `12345678`), повторения (`aaaabbbb`), инкременты (`abcdefgh`).

**Конкретно в чём проблема**  
`12345678` имеет уникальность 100% (все 8 цифр разные) → `uniquenessRatio = 1.0 > 0.7` → +10 к score. Итого: длина >=8 (+10) + цифры >=3 (+20) + уникальность (+10) = **40** — что соответствует "средней сложности", хотя пароль очевиден.

**Путь к файлу:**  
`Shared/BarkFluff.Shared.SecurityUtilities/SecurityUtilities.cs` : 40–44

```csharp
// "12345678" → uniquenessRatio = 8/8 = 1.0 → +10 бонус за "уникальность"
// Хотя это классическая последовательность клавиатуры
double uniquenessRatio = password.Distinct().Count() / (double)password.Length;
if (uniquenessRatio > 0.7)
    score += 10;
```

**Варианты решения**

Добавить штраф за последовательности.

```csharp
// Штраф за последовательные символы (клавиатурные паттерны)
private static readonly string[] SequentialPatterns =
    ["qwerty", "asdfgh", "zxcvbn", "qazwsx", "12345678", "abcdefgh", "87654321"];

private static int GetSequencePenalty(string password)
{
    string lower = password.ToLowerInvariant();
    foreach (var pattern in SequentialPatterns)
    {
        // Проверяем вхождение паттерна длиной >=4
        for (int len = 4; len <= pattern.Length; len++)
        {
            if (lower.Contains(pattern[..len]))
                return -15; // штраф за найденный паттерн
        }
    }
    return 0;
}

// В EvaluatePasswordStrength добавить:
score += GetSequencePenalty(password);
score = Math.Max(0, score); // score не может быть отрицательным
return Math.Min(score, 100);
```

---

## ⚪ Прочее / Качество кода

---

### QA-01 — Класс не sealed и не static, методы только static

**Проблема / Описание**  
`SecurityUtilities` объявлен как `public class`, но содержит только статические методы. Класс можно инстанциировать (`new SecurityUtilities()`) и наследовать, хотя это лишено смысла.

**Конкретно в чём проблема**  
Нарушение принципа наименьшего удивления. IDE не подскажет, что создавать экземпляр бессмысленно.

**Путь к файлу:**  
`Shared/BarkFluff.Shared.SecurityUtilities/SecurityUtilities.cs` : 3

```csharp
// Текущее объявление — инстанциирование возможно, но бессмысленно:
public class SecurityUtilities
{
    public static int EvaluatePasswordStrength(string password) { ... }
    public static (string, string) GetPasswordStrengthMessage(int score) { ... }
}

var x = new SecurityUtilities(); // компилируется, но зачем?
```

**Варианты решения**

```csharp
// Сделать класс static — запрещает инстанциирование и наследование явно
public static class SecurityUtilities
{
    public static int EvaluatePasswordStrength(string password) { ... }
    public static (string message, string colorHex) GetPasswordStrengthMessage(int score) { ... }
}
```

---

### QA-02 — Нет документации XML на публичных методах

**Проблема / Описание**  
Публичные методы shared-библиотеки не имеют XML-документации (`/// <summary>`). Это затрудняет использование библиотеки в других проектах — IntelliSense не показывает описание параметров и возвращаемых значений.

**Путь к файлу:**  
`Shared/BarkFluff.Shared.SecurityUtilities/SecurityUtilities.cs` : 5, 48

```csharp
// Нет документации:
public static int EvaluatePasswordStrength(string password)
public static (string message, string colorHex) GetPasswordStrengthMessage(int score)
```

**Варианты решения**

```csharp
/// <summary>
/// Оценивает надёжность пароля по структурным критериям.
/// </summary>
/// <param name="password">Пароль для оценки.</param>
/// <returns>
/// Число от 0 до 100: 0–19 — очень слабый, 20–39 — слабый,
/// 40–59 — средний, 60–79 — хороший, 80–100 — надёжный.
/// </returns>
public static int EvaluatePasswordStrength(string password)

/// <summary>
/// Возвращает локализованное (ru-RU) сообщение и HEX-цвет для отображения силы пароля в UI.
/// </summary>
/// <param name="score">Score от 0 до 100, полученный из <see cref="EvaluatePasswordStrength"/>.</param>
/// <returns>Кортеж (message, colorHex) для отображения в интерфейсе.</returns>
public static (string message, string colorHex) GetPasswordStrengthMessage(int score)
```

---

### QA-03 — Нет юнит-тестов

**Проблема / Описание**  
Для `SecurityUtilities` отсутствует проект с юнит-тестами. Алгоритм оценки пароля — критичная для безопасности логика, которая должна быть покрыта тестами для предотвращения регрессий.

**Конкретно в чём проблема**  
Любое изменение весов или порогов может незаметно нарушить ожидаемое поведение (например, пользователи с существующими паролями внезапно не пройдут re-валидацию при смене пароля).

**Варианты решения**

```csharp
// BarkFluff.Shared.SecurityUtilities.Tests/SecurityUtilitiesTests.cs
public class EvaluatePasswordStrengthTests
{
    [Fact]
    public void EmptyPassword_ReturnsZero()
        => Assert.Equal(0, SecurityUtilities.EvaluatePasswordStrength(""));

    [Theory]
    [InlineData("abc",        0)]  // короче MinLength → 0 (после SEC-03 fix)
    [InlineData("abcdefgh",  10)]  // только длина >=8, нижний регистр
    [InlineData("Abcdefg1!",  60)] // должен ≥ MinStrengthScore
    [InlineData("P@ssw0rd1",   0)] // словарный пароль → 0 (после SEC-02 fix)
    public void KnownPasswords_ReturnExpectedScore(string password, int expectedScore)
        => Assert.Equal(expectedScore, SecurityUtilities.EvaluatePasswordStrength(password));

    [Fact]
    public void SpaceIsNotSpecialChar_DoesNotBoostScore()
    {
        int withSpace    = SecurityUtilities.EvaluatePasswordStrength("Abcdefg1 ");
        int withoutSpace = SecurityUtilities.EvaluatePasswordStrength("Abcdefg1");
        Assert.Equal(withoutSpace, withSpace); // пробел не должен давать бонус
    }
}
```

---

## Итоговая таблица

| ID     | Категория    | Критичность | Файл                                          |
| ------ | ------------ | ----------- | --------------------------------------------- |
| SEC-01 | Безопасность | 🔴 Высокая  | SecurityUtilities.cs + Validators.cpp         |
| SEC-02 | Безопасность | 🔴 Высокая  | SecurityUtilities.cs                          |
| SEC-03 | Безопасность | 🟠 Средняя  | SecurityUtilities.cs                          |
| SEC-04 | Безопасность | 🟠 Средняя  | SecurityUtilities.cs + PasswordValidator.cs   |
| OPT-01 | Оптимизация  | 🟡 Низкая   | SecurityUtilities.cs                          |
| OPT-02 | Оптимизация  | 🟡 Низкая   | CreateAccount.xaml.cs (🔄 PasswordReset.xaml.cs уже исправлен 2026-05-18) |
| OPT-03 | Оптимизация  | 🟡 Низкая   | CreateAccount.xaml.cs + PasswordReset.xaml.cs |
| BUG-01 | Баг          | 🟠 Средняя  | SecurityUtilities.cs + PasswordValidator.cs   |
| BUG-02 | Баг          | 🟡 Низкая   | PasswordValidator.cs                          |
| BUG-03 | Баг          | 🟠 Средняя  | SecurityUtilities.cs                          |
| QA-01  | Качество     | ⚪ Низкая    | SecurityUtilities.cs                          |
| QA-02  | Качество     | ⚪ Низкая    | SecurityUtilities.cs                          |
| QA-03  | Качество     | 🟠 Средняя  | — (тестов нет)                                |
