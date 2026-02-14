//
//  LiquidGlassGuide.swift
//  Barkfluff
//
//  Полный справочник по Liquid Glass (macOS 26 / iOS 26)
//  Источники:
//    - WWDC25 Session 219: Meet Liquid Glass
//    - WWDC25 Session 323: Build a SwiftUI app with the new design
//    - WWDC25 Session 356: Get to know the new design system
//    - Apple Developer Documentation: Applying Liquid Glass to custom views
//    - https://github.com/conorluddy/LiquidGlassReference
//
//  Последнее обновление: 2026-02-14
//

// ============================================================================
// MARK: - 1. ЧТО ТАКОЕ LIQUID GLASS
// ============================================================================
//
// Liquid Glass — новый дизайн-язык Apple (WWDC 2025).
// Полупрозрачный материал с:
//   - Динамическим размытием, адаптирующимся к фону
//   - Преломлением света в реальном времени (lensing)
//   - Зеркальными бликами, реагирующими на движение устройства
//   - Адаптивными тенями
//   - Интерактивными жестами (масштабирование, bounce, shimmer)
//
// Liquid Glass — ТОЛЬКО для навигационного слоя, который парит над контентом.
// НИКОГДА не применять к самому контенту (списки, таблицы, медиа).
//

// ============================================================================
// MARK: - 2. ОСНОВНОЙ МОДИФИКАТОР: .glassEffect()
// ============================================================================
//
// Сигнатура:
//
//   func glassEffect<S: Shape>(
//       _ glass: Glass = .regular,
//       in shape: S = DefaultGlassEffectShape,  // по умолчанию Capsule
//       isEnabled: Bool = true
//   ) -> some View
//
// Простейшее использование:
//
//   Text("Hello, Glass!")
//       .padding()
//       .glassEffect()
//
// С параметрами:
//
//   Text("Custom")
//       .padding()
//       .glassEffect(.clear, in: RoundedRectangle(cornerRadius: 16))
//

// ============================================================================
// MARK: - 3. ВАРИАНТЫ СТЕКЛА (Glass)
// ============================================================================
//
// ┌────────────┬──────────────────────────────────┬───────────────┬──────────────────────────────┐
// │ Вариант    │ Описание                         │ Прозрачность  │ Когда использовать           │
// ├────────────┼──────────────────────────────────┼───────────────┼──────────────────────────────┤
// │ .regular   │ Стандартный, используется        │ Средняя       │ Тулбары, кнопки, навбары,    │
// │            │ повсеместно в системе            │               │ любые UI-контролы            │
// ├────────────┼──────────────────────────────────┼───────────────┼──────────────────────────────┤
// │ .clear     │ Более прозрачный, без ярко       │ Высокая       │ Мелкие контролы поверх       │
// │            │ выраженной стеклянной границы    │               │ фото/карт/медиа-контента     │
// ├────────────┼──────────────────────────────────┼───────────────┼──────────────────────────────┤
// │ .identity  │ Отключает эффект (прозрачно)     │ Нет эффекта   │ Условное вкл/выкл стекла     │
// └────────────┴──────────────────────────────────┴───────────────┴──────────────────────────────┘
//
// Условное переключение:
//
//   .glassEffect(isGlassEnabled ? .regular : .identity)
//
// ВАЖНО: НЕ смешивать .regular и .clear в одном наборе контролов —
// это создает путаницу в визуальной иерархии.
//

// ============================================================================
// MARK: - 4. ФОРМЫ (Shapes)
// ============================================================================
//
// По умолчанию — Capsule. Можно задать любую форму:
//
//   .glassEffect(.regular, in: .capsule)
//   .glassEffect(.regular, in: .circle)
//   .glassEffect(.regular, in: .ellipse)
//   .glassEffect(.regular, in: RoundedRectangle(cornerRadius: 16))
//   .glassEffect(.regular, in: .rect(cornerRadius: .containerConcentric))
//
// .containerConcentric — форма автоматически подстраивается под углы
// родительского контейнера на разных экранах и окнах.
//

// ============================================================================
// MARK: - 5. ТОНИРОВАНИЕ (Tinting)
// ============================================================================
//
// Добавляет цветовой оттенок к стеклу:
//
//   .glassEffect(.regular.tint(.blue))
//   .glassEffect(.regular.tint(.purple.opacity(0.6)))
//   .glassEffect(.clear.tint(.red))
//
// ПРАВИЛО: Тонирование — только для передачи смысла (состояние, иерархия),
// а НЕ для декорации.
//

// ============================================================================
// MARK: - 6. ИНТЕРАКТИВНЫЙ РЕЖИМ (Interactive)
// ============================================================================
//
// Добавляет масштабирование, bounce, shimmer, подсветку от касания:
//
//   .glassEffect(.regular.interactive())
//   .glassEffect(.clear.interactive())
//
// Комбинация:
//
//   .glassEffect(.regular.tint(.blue).interactive())
//
// Интерактивный режим усиливает отражение фона и реакцию на жесты.
// ПРИМЕЧАНИЕ: На стандартных кнопках НЕ нужно включать — они уже реагируют.
// Используется в основном на iOS.
//

// ============================================================================
// MARK: - 7. СТИЛИ КНОПОК
// ============================================================================
//
// Два встроенных стиля для кнопок:
//
//   .buttonStyle(.glass)           — полупрозрачная, показывает фон
//   .buttonStyle(.glassProminent)  — непрозрачная, но с glass-эффектом (для primary action)
//
// Пример:
//
//   Button("Отправить") { send() }
//       .buttonStyle(.glassProminent)
//       .tint(.blue)
//
// Круглая кнопка:
//
//   Button { } label: {
//       Image(systemName: "heart.fill")
//           .frame(width: 44, height: 44)
//   }
//   .buttonStyle(.glass)
//   .buttonBorderShape(.circle)
//   .clipShape(Circle())  // ← Обязательно! Иначе форма "плывёт" при нажатии
//
// Размеры: .controlSize(.mini / .small / .medium / .large)
//
// ВАЖНО: В тулбарах стекло применяется автоматически — не надо добавлять вручную.
// Для кастомных кнопок в контенте — стиль нужно указывать явно.
//

// ============================================================================
// MARK: - 8. GlassEffectContainer — ГРУППИРОВКА
// ============================================================================
//
// Стекло НЕ МОЖЕТ сэмплировать другое стекло.
// Чтобы стеклянные элементы корректно отражали друг друга — оберните в контейнер:
//
//   GlassEffectContainer {
//       HStack(spacing: 20) {
//           Image(systemName: "pencil")
//               .frame(width: 44, height: 44)
//               .glassEffect(.regular.interactive())
//
//           Image(systemName: "eraser")
//               .frame(width: 44, height: 44)
//               .glassEffect(.regular.interactive())
//       }
//   }
//
// БОНУС: Контейнер объединяет все glass-элементы в один CABackdropLayer,
// что ЗНАЧИТЕЛЬНО улучшает производительность (каждый отдельный glass —
// это 3 offscreen-текстуры).
//
// Параметр spacing — расстояние, при котором формы начинают морфиться:
//
//   GlassEffectContainer(spacing: 40.0) {
//       // элементы ближе 40pt будут сливаться визуально
//   }
//

// ============================================================================
// MARK: - 9. МОРФИНГ: glassEffectID
// ============================================================================
//
// Позволяет стеклянным элементам плавно трансформироваться друг в друга:
//
//   @Namespace private var namespace
//   @State private var isExpanded = false
//
//   GlassEffectContainer(spacing: 30) {
//       Button(isExpanded ? "Свернуть" : "Развернуть") {
//           withAnimation(.bouncy) {
//               isExpanded.toggle()
//           }
//       }
//       .glassEffect()
//       .glassEffectID("toggle", in: namespace)
//
//       if isExpanded {
//           Button("Действие 1") { }
//               .glassEffect()
//               .glassEffectID("action1", in: namespace)
//
//           Button("Действие 2") { }
//               .glassEffect()
//               .glassEffectID("action2", in: namespace)
//       }
//   }
//
// Требования:
//   - Элементы внутри одного GlassEffectContainer
//   - У каждого уникальный glassEffectID в общем @Namespace
//   - Анимация через withAnimation {} при изменении состояния
//   - .glassEffect() применяется ПЕРЕД .glassEffectID()
//

// ============================================================================
// MARK: - 10. glassEffectUnion — ОБЪЕДИНЕНИЕ НА РАССТОЯНИИ
// ============================================================================
//
// Объединяет glass-элементы, которые далеко друг от друга:
//
//   @Namespace var tools
//
//   Image(systemName: "pencil")
//       .glassEffect(.regular.interactive())
//       .glassEffectUnion(id: "tools", namespace: tools)
//
//   Image(systemName: "eraser")
//       .glassEffect(.regular.interactive())
//       .glassEffectUnion(id: "tools", namespace: tools)
//
// Ограничение: работает ТОЛЬКО когда элементы имеют одинаковые effect types,
// похожие формы и одинаковые идентификаторы.
//

// ============================================================================
// MARK: - 11. SCROLL EDGE EFFECT
// ============================================================================
//
// Тулбар автоматически адаптирует цвет при скролле:
//
//   .scrollEdgeEffectStyle(.automatic)  // по умолчанию
//   .scrollEdgeEffectStyle(.sharp)      // резкий переход
//   .scrollEdgeEffectStyle(.subtle)     // мягкий переход
//
// ВАЖНО: Уберите кастомные фоны и затемнение за тулбарами —
// они мешают scroll edge effect.
//

// ============================================================================
// MARK: - 12. ACCESSIBILITY (Доступность)
// ============================================================================
//
// Автоматические адаптации (без кода):
//   - Reduce Transparency: увеличивает матовость стекла
//   - Increased Contrast: использует чёткие цвета и границы
//   - Reduce Motion: приглушает анимации
//   - Tinted Mode (iOS 26.1+): пользователь управляет прозрачностью
//
// Ручное управление:
//
//   @Environment(\.accessibilityReduceTransparency) var reduceTransparency
//
//   Text("Текст")
//       .padding()
//       .glassEffect(reduceTransparency ? .identity : .regular)
//
// НЕЛЬЗЯ предполагать что пользователь хочет "максимум стекла" —
// уважайте настройки доступности.
//

// ============================================================================
// MARK: - 13. ПРАВИЛА И ЗАПРЕТЫ
// ============================================================================
//
// ✅ ДЕЛАТЬ:
//   - Использовать стандартные компоненты (NavigationStack, TabView, toolbar)
//   - Применять glass к навигационному слою (тулбары, кнопки, панели)
//   - Группировать glass-элементы в GlassEffectContainer
//   - Тестировать с Reduce Transparency и Increased Contrast
//   - Использовать .containerConcentric для автоматических углов
//   - Убрать кастомные фоны за тулбарами (мешают scroll edge effect)
//   - Использовать .contentShape() для правильного hit-testing на кнопках
//
// ❌ НЕ ДЕЛАТЬ:
//   - НЕ применять glass к контенту (списки, таблицы, ячейки, медиа)
//   - НЕ ставить glass на всё подряд — только ключевые UI-элементы
//   - НЕ смешивать .regular и .clear в одном наборе контролов
//   - НЕ ставить glass поверх glass без GlassEffectContainer
//   - НЕ использовать тонирование для декорации (только для смысла)
//   - НЕ помещать Menu внутрь GlassEffectContainer (баг iOS 26.1)
//   - НЕ применять rotationEffect() к views с glass (форма искажается)
//   - НЕ делать много отдельных glass-элементов без контейнера (каждый = 3 offscreen-текстуры)
//

// ============================================================================
// MARK: - 14. ИЗВЕСТНЫЕ БАГИ И ОБХОДНЫЕ ПУТИ
// ============================================================================
//
// 1. rotationEffect() + glassEffect → форма искажается при анимации
//    Решение: UIViewRepresentable с UIVisualEffectView + UIGlassEffect
//
// 2. Menu + glassEffect → морфинг ломается (iOS 26.0-26.1)
//    Решение: кастомный ButtonStyle, который применяет glass к label
//
// 3. Hit-testing — кнопка реагирует только на иконку, не на всю glass-область
//    Решение: .contentShape(Capsule()) или .contentShape(Circle())
//
// 4. Menu внутри GlassEffectContainer — ломает морфинг (iOS 26.1)
//    Решение: вынести Menu за пределы контейнера
//
// 5. Несколько glass без контейнера — проблемы с производительностью
//    Решение: всегда оборачивать в GlassEffectContainer
//

// ============================================================================
// MARK: - 15. ПОЛНЫЕ ПРИМЕРЫ
// ============================================================================

import SwiftUI

// MARK: - Пример 1: Базовое применение

#Preview("1. Basic Glass") {
    VStack(spacing: 20) {
        // Текст с glass
        Text("Regular Glass")
            .padding()
            .glassEffect()

        // Clear glass
        Text("Clear Glass")
            .padding()
            .glassEffect(.clear, in: .capsule)

        // Glass с тонированием
        Text("Tinted Glass")
            .padding()
            .glassEffect(.regular.tint(.blue))

        // Кастомная форма
        Text("Rounded Rectangle")
            .padding()
            .glassEffect(.regular, in: RoundedRectangle(cornerRadius: 12))
    }
    .padding(40)
    .frame(width: 300, height: 400)
    .background(
        LinearGradient(colors: [.blue, .purple], startPoint: .top, endPoint: .bottom)
    )
}

// MARK: - Пример 2: Кнопки

#Preview("2. Glass Buttons") {
    VStack(spacing: 20) {
        // Glass кнопка (вторичное действие)
        Button("Отмена") { }
            .buttonStyle(.glass)

        // GlassProminent кнопка (основное действие)
        Button("Отправить") { }
            .buttonStyle(.glassProminent)
            .tint(.blue)

        // Круглая кнопка-иконка
        Button { } label: {
            Image(systemName: "plus")
                .font(.title2)
                .frame(width: 44, height: 44)
        }
        .buttonStyle(.glass)
        .buttonBorderShape(.circle)
        .clipShape(Circle())
    }
    .padding(40)
    .frame(width: 300, height: 300)
    .background(Color.gray.opacity(0.2))
}

// MARK: - Пример 3: GlassEffectContainer + Морфинг

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

#Preview("3. Morphing Glass") {
    MorphingGlassDemo()
        .padding(40)
        .frame(width: 400, height: 200)
        .background(
            LinearGradient(colors: [.orange, .pink], startPoint: .topLeading, endPoint: .bottomTrailing)
        )
}

// MARK: - Пример 4: Текстовое поле с glass (как в BarkFluff)

#Preview("4. Glass TextField") {
    VStack {
        Spacer()
        HStack(spacing: 12) {
            Button { } label: {
                Image(systemName: "plus")
                    .font(.title3)
                    .foregroundStyle(.secondary)
                    .frame(width: 32, height: 32)
            }
            .buttonStyle(.plain)

            TextField("Сообщение...", text: .constant(""))
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
        .padding()
    }
    .frame(width: 400, height: 300)
    .background(Color.gray.opacity(0.15))
}
