# BarkFluff.Shared.SecurityUtilities

Библиотека утилит безопасности (.NET 10), используемая клиентскими проектами (WPF).

Расположение: `Shared/BarkFluff.Shared.SecurityUtilities/`

## Содержимое

Единственный файл — `SecurityUtilities.cs`, класс `SecurityUtilities`:

- `EvaluatePasswordStrength(string password) → int` — score 0–100
- `GetPasswordStrengthMessage(int score) → (string message, string colorHex)` — локализованное сообщение (русский) и hex-цвет для UI

## Scoring Logic

| Критерий | Очки |
|----------|------|
| Длина ≥16 | +30 |
| Длина ≥12 | +20 |
| Длина ≥8 | +10 |
| Оба регистра | +20 / один +10 |
| ≥3 цифры | +20 / ≥1 +10 |
| ≥2 спецсимвола | +20 / 1 +10 |
| Уникальность >70% | +10 / >40% +5 |

Максимум — 100 (`Math.Min`).

## Зависимости

Нет внешних зависимостей. Подключается как проектная ссылка.
