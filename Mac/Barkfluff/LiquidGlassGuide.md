# Liquid Glass — Полный справочник (macOS 26 / iOS 26)

> Источники:
> - [WWDC25 Session 219: Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/219/)
> - [WWDC25 Session 323: Build a SwiftUI app with the new design](https://developer.apple.com/videos/play/wwdc2025/323/)
> - [WWDC25 Session 356: Get to know the new design system](https://developer.apple.com/videos/play/wwdc2025/356/)
> - [Apple Developer: Applying Liquid Glass to custom views](https://developer.apple.com/documentation/SwiftUI/Applying-Liquid-Glass-to-custom-views)
> - [LiquidGlassReference (GitHub)](https://github.com/conorluddy/LiquidGlassReference)
> - [Glassifying custom SwiftUI views — Swift with Majid](https://swiftwithmajid.com/2025/07/16/glassifying-custom-swiftui-views/)
> - [Adopting Liquid Glass: Experiences and Pitfalls](https://juniperphoton.substack.com/p/adopting-liquid-glass-experiences)
>
> Последнее обновление: 2026-02-14

---

## 1. Что такое Liquid Glass

Liquid Glass — новый дизайн-язык Apple (WWDC 2025). Полупрозрачный материал с:

- Динамическим размытием, адаптирующимся к фону
- Преломлением света в реальном времени (lensing)
- Зеркальными бликами, реагирующими на движение устройства
- Адаптивными тенями
- Интерактивными жестами (масштабирование, bounce, shimmer)

**Liquid Glass — ТОЛЬКО для навигационного слоя, который парит над контентом. НИКОГДА не применять к самому контенту (списки, таблицы, медиа).**

---

## 2. Основной модификатор: `.glassEffect()`

### Сигнатура

```swift
func glassEffect<S: Shape>(
    _ glass: Glass = .regular,
    in shape: S = DefaultGlassEffectShape,  // по умолчанию Capsule
    isEnabled: Bool = true
) -> some View
```

### Использование

```swift
// Простейшее — все параметры по умолчанию
Text("Hello, Glass!")
    .padding()
    .glassEffect()

// С параметрами
Text("Custom")
    .padding()
    .glassEffect(.clear, in: RoundedRectangle(cornerRadius: 16))
```

---

## 3. Варианты стекла (Glass)

| Вариант | Описание | Прозрачность | Когда использовать |
|---------|----------|-------------|-------------------|
| `.regular` | Стандартный, используется повсеместно в системе | Средняя | Тулбары, кнопки, навбары, любые UI-контролы |
| `.clear` | Более прозрачный, без ярко выраженной стеклянной границы | Высокая | Мелкие контролы поверх фото/карт/медиа-контента |
| `.identity` | Отключает эффект (прозрачно) | Нет эффекта | Условное вкл/выкл стекла |

### Условное переключение

```swift
.glassEffect(isGlassEnabled ? .regular : .identity)
```

**ВАЖНО:** НЕ смешивать `.regular` и `.clear` в одном наборе контролов — это создает путаницу в визуальной иерархии.

---

## 4. Формы (Shapes)

По умолчанию — Capsule. Можно задать любую форму:

```swift
.glassEffect(.regular, in: .capsule)
.glassEffect(.regular, in: .circle)
.glassEffect(.regular, in: .ellipse)
.glassEffect(.regular, in: RoundedRectangle(cornerRadius: 16))
.glassEffect(.regular, in: .rect(cornerRadius: .containerConcentric))
```

`.containerConcentric` — форма автоматически подстраивается под углы родительского контейнера на разных экранах и окнах.

---

## 5. Тонирование (Tinting)

Добавляет цветовой оттенок к стеклу:

```swift
.glassEffect(.regular.tint(.blue))
.glassEffect(.regular.tint(.purple.opacity(0.6)))
.glassEffect(.clear.tint(.red))
```

**ПРАВИЛО:** Тонирование — только для передачи смысла (состояние, иерархия), а НЕ для декорации.

---

## 6. Интерактивный режим (Interactive)

Добавляет масштабирование, bounce, shimmer, подсветку от касания:

```swift
.glassEffect(.regular.interactive())
.glassEffect(.clear.interactive())

// Комбинация
.glassEffect(.regular.tint(.blue).interactive())
```

Интерактивный режим усиливает отражение фона и реакцию на жесты.

**Примечание:** На стандартных кнопках НЕ нужно включать — они уже реагируют. Используется в основном на iOS.

---

## 7. Стили кнопок

Два встроенных стиля:

| Стиль | Описание | Для чего |
|-------|----------|----------|
| `.buttonStyle(.glass)` | Полупрозрачная, показывает фон | Вторичные действия |
| `.buttonStyle(.glassProminent)` | Непрозрачная, но с glass-эффектом | Primary action |

### Примеры

```swift
// Основное действие
Button("Отправить") { send() }
    .buttonStyle(.glassProminent)
    .tint(.blue)

// Круглая кнопка-иконка
Button { } label: {
    Image(systemName: "heart.fill")
        .frame(width: 44, height: 44)
}
.buttonStyle(.glass)
.buttonBorderShape(.circle)
.clipShape(Circle())  // Обязательно! Иначе форма "плывёт" при нажатии
```

### Размеры

```swift
.controlSize(.mini)
.controlSize(.small)
.controlSize(.medium)
.controlSize(.large)
```

**ВАЖНО:** В тулбарах стекло применяется автоматически — не надо добавлять вручную. Для кастомных кнопок в контенте — стиль нужно указывать явно.

---

## 8. GlassEffectContainer — группировка

Стекло **НЕ МОЖЕТ** сэмплировать другое стекло. Чтобы стеклянные элементы корректно отражали друг друга — оберните в контейнер:

```swift
GlassEffectContainer {
    HStack(spacing: 20) {
        Image(systemName: "pencil")
            .frame(width: 44, height: 44)
            .glassEffect(.regular.interactive())

        Image(systemName: "eraser")
            .frame(width: 44, height: 44)
            .glassEffect(.regular.interactive())
    }
}
```

### Производительность

Контейнер объединяет все glass-элементы в один `CABackdropLayer`, что **значительно** улучшает производительность. Каждый отдельный glass — это 3 offscreen-текстуры.

### Параметр spacing

Расстояние, при котором формы начинают морфиться:

```swift
GlassEffectContainer(spacing: 40.0) {
    // элементы ближе 40pt будут сливаться визуально
}
```

---

## 9. Морфинг: `glassEffectID`

Позволяет стеклянным элементам плавно трансформироваться друг в друга:

```swift
@Namespace private var namespace
@State private var isExpanded = false

GlassEffectContainer(spacing: 30) {
    Button(isExpanded ? "Свернуть" : "Развернуть") {
        withAnimation(.bouncy) {
            isExpanded.toggle()
        }
    }
    .glassEffect()
    .glassEffectID("toggle", in: namespace)

    if isExpanded {
        Button("Действие 1") { }
            .glassEffect()
            .glassEffectID("action1", in: namespace)

        Button("Действие 2") { }
            .glassEffect()
            .glassEffectID("action2", in: namespace)
    }
}
```

### Требования

- Элементы внутри одного `GlassEffectContainer`
- У каждого уникальный `glassEffectID` в общем `@Namespace`
- Анимация через `withAnimation {}` при изменении состояния
- `.glassEffect()` применяется **ПЕРЕД** `.glassEffectID()`

---

## 10. `glassEffectUnion` — объединение на расстоянии

Объединяет glass-элементы, которые далеко друг от друга:

```swift
@Namespace var tools

Image(systemName: "pencil")
    .glassEffect(.regular.interactive())
    .glassEffectUnion(id: "tools", namespace: tools)

Image(systemName: "eraser")
    .glassEffect(.regular.interactive())
    .glassEffectUnion(id: "tools", namespace: tools)
```

**Ограничение:** работает ТОЛЬКО когда элементы имеют одинаковые effect types, похожие формы и одинаковые идентификаторы.

---

## 11. Scroll Edge Effect

Тулбар автоматически адаптирует цвет при скролле:

```swift
.scrollEdgeEffectStyle(.automatic)  // по умолчанию
.scrollEdgeEffectStyle(.sharp)      // резкий переход
.scrollEdgeEffectStyle(.subtle)     // мягкий переход
```

**ВАЖНО:** Уберите кастомные фоны и затемнение за тулбарами — они мешают scroll edge effect.

---

## 12. Accessibility (Доступность)

### Автоматические адаптации (без кода)

- **Reduce Transparency**: увеличивает матовость стекла
- **Increased Contrast**: использует чёткие цвета и границы
- **Reduce Motion**: приглушает анимации
- **Tinted Mode** (iOS 26.1+): пользователь управляет прозрачностью

### Ручное управление

```swift
@Environment(\.accessibilityReduceTransparency) var reduceTransparency

Text("Текст")
    .padding()
    .glassEffect(reduceTransparency ? .identity : .regular)
```

НЕЛЬЗЯ предполагать что пользователь хочет "максимум стекла" — уважайте настройки доступности.

---

## 13. Правила и запреты

### ДЕЛАТЬ

- Использовать стандартные компоненты (`NavigationStack`, `TabView`, toolbar)
- Применять glass к навигационному слою (тулбары, кнопки, панели)
- Группировать glass-элементы в `GlassEffectContainer`
- Тестировать с Reduce Transparency и Increased Contrast
- Использовать `.containerConcentric` для автоматических углов
- Убрать кастомные фоны за тулбарами (мешают scroll edge effect)
- Использовать `.contentShape()` для правильного hit-testing на кнопках

### НЕ ДЕЛАТЬ

- НЕ применять glass к контенту (списки, таблицы, ячейки, медиа)
- НЕ ставить glass на всё подряд — только ключевые UI-элементы
- НЕ смешивать `.regular` и `.clear` в одном наборе контролов
- НЕ ставить glass поверх glass без `GlassEffectContainer`
- НЕ использовать тонирование для декорации (только для смысла)
- НЕ помещать `Menu` внутрь `GlassEffectContainer` (баг iOS 26.1)
- НЕ применять `rotationEffect()` к views с glass (форма искажается)
- НЕ делать много отдельных glass-элементов без контейнера (каждый = 3 offscreen-текстуры)

---

## 14. Известные баги и обходные пути

### 1. `rotationEffect()` + `glassEffect`
**Проблема:** Форма искажается при анимации вращения.
**Решение:** `UIViewRepresentable` с `UIVisualEffectView` + `UIGlassEffect`.

### 2. `Menu` + `glassEffect`
**Проблема:** Морфинг ломается (iOS 26.0-26.1).
**Решение:** Кастомный `ButtonStyle`, который применяет glass к label.

### 3. Hit-testing
**Проблема:** Кнопка реагирует только на иконку, не на всю glass-область.
**Решение:** `.contentShape(Capsule())` или `.contentShape(Circle())`.

### 4. `Menu` внутри `GlassEffectContainer`
**Проблема:** Ломает морфинг (iOS 26.1).
**Решение:** Вынести `Menu` за пределы контейнера.

### 5. Много glass без контейнера
**Проблема:** Проблемы с производительностью.
**Решение:** Всегда оборачивать в `GlassEffectContainer`.

---

## 15. Полные примеры кода

### Базовое применение

```swift
VStack(spacing: 20) {
    Text("Regular Glass")
        .padding()
        .glassEffect()

    Text("Clear Glass")
        .padding()
        .glassEffect(.clear, in: .capsule)

    Text("Tinted Glass")
        .padding()
        .glassEffect(.regular.tint(.blue))

    Text("Rounded Rectangle")
        .padding()
        .glassEffect(.regular, in: RoundedRectangle(cornerRadius: 12))
}
```

### Кнопки

```swift
VStack(spacing: 20) {
    Button("Отмена") { }
        .buttonStyle(.glass)

    Button("Отправить") { }
        .buttonStyle(.glassProminent)
        .tint(.blue)

    Button { } label: {
        Image(systemName: "plus")
            .font(.title2)
            .frame(width: 44, height: 44)
    }
    .buttonStyle(.glass)
    .buttonBorderShape(.circle)
    .clipShape(Circle())
}
```

### GlassEffectContainer + Морфинг

```swift
struct MorphingGlassDemo: View {
    @State private var isExpanded = false
    @Namespace private var ns

    var body: some View {
        GlassEffectContainer(spacing: 30) {
            HStack(spacing: 16) {
                Button {
                    withAnimation(.bouncy) { isExpanded.toggle() }
                } label: {
                    Image(systemName: isExpanded ? "xmark" : "plus")
                        .font(.title2)
                        .frame(width: 44, height: 44)
                }
                .buttonStyle(.plain)
                .glassEffect(.regular.interactive(), in: .circle)
                .glassEffectID("main", in: ns)

                if isExpanded {
                    Button { } label: {
                        Image(systemName: "photo")
                            .font(.title3)
                            .frame(width: 40, height: 40)
                    }
                    .buttonStyle(.plain)
                    .glassEffect(.regular.interactive(), in: .circle)
                    .glassEffectID("photo", in: ns)

                    Button { } label: {
                        Image(systemName: "doc")
                            .font(.title3)
                            .frame(width: 40, height: 40)
                    }
                    .buttonStyle(.plain)
                    .glassEffect(.regular.interactive(), in: .circle)
                    .glassEffectID("doc", in: ns)
                }
            }
        }
    }
}
```

### Текстовое поле с glass (как в BarkFluff)

```swift
HStack(spacing: 12) {
    Button { } label: {
        Image(systemName: "plus")
            .font(.title3)
            .foregroundStyle(.secondary)
            .frame(width: 32, height: 32)
    }
    .buttonStyle(.plain)

    TextField("Сообщение...", text: $text, axis: .vertical)
        .textFieldStyle(.plain)
        .padding(.horizontal, 16)
        .padding(.vertical, 8)
        .glassEffect(.regular, in: .capsule)

    Button { } label: {
        Image(systemName: "arrow.up")
            .font(.title3)
            .fontWeight(.semibold)
            .foregroundStyle(.white)
            .frame(width: 32, height: 32)
            .background(Circle().fill(.blue))
    }
    .buttonStyle(.plain)
}
```
