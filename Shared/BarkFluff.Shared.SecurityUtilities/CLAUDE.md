# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

`BarkFluff.Shared.SecurityUtilities` — разделяемая библиотека (.NET 10) с утилитами безопасности, используемая клиентскими проектами (WPF) и, возможно, бэкендом.

## Build

```bash
dotnet build BarkFluff.Shared.SecurityUtilities.csproj
```

## Contents

Единственный файл — `SecurityUtilities.cs`, класс `SecurityUtilities` с двумя статическими методами:

- `EvaluatePasswordStrength(string password) → int` — возвращает score 0–100 на основе длины, регистра, цифр, спецсимволов и уникальности символов.
- `GetPasswordStrengthMessage(int score) → (string message, string colorHex)` — возвращает локализованное сообщение (на русском) и hex-цвет для отображения силы пароля в UI.

## Scoring Logic

| Критерий | Очки |
|----------|------|
| Длина ≥16 | +30 |
| Длина ≥12 | +20 |
| Длина ≥8 | +10 |
| Оба регистра | +20 / один регистр +10 |
| ≥3 цифры | +20 / ≥1 цифра +10 |
| ≥2 спецсимвола | +20 / 1 спецсимвол +10 |
| Уникальность >70% | +10 / >40% +5 |

Максимум — 100 (ограничен `Math.Min`).

## Usage Pattern

Подключается как проектная ссылка в других решениях. Нет внешних зависимостей, нет NuGet-пакетов.
