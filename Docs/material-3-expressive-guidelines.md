# Material 3 Expressive — Полное руководство

> Источник: [m3.material.io](https://m3.material.io)
> Актуально на: **май 2026**
> Material 3 Expressive — текущая итерация Material Design, анонсированная на Google I/O в мае 2025 и развёрнутая с Android 16 QPR1 (сентябрь 2025) и Wear OS 6.
> Это **не новая версия системы** ("не M4") — это расширение M3 новыми компонентами, физической моделью движения, расширенной библиотекой форм и новыми принципами визуальной иерархии.

---

## Содержание

1. [Что такое M3 Expressive](#intro)
2. [5 фундаментальных тактик](#tactics)
3. [Foundations — Принципы](#principles)
4. [Adaptive Design — Адаптивный дизайн](#adaptive)
5. [Building for All — Доступность](#accessibility)
6. [Content Design — Контент и тексты](#content)
7. [Customization — Кастомизация](#customization)
8. [Design Tokens — Токены](#tokens)
9. [Interaction — Взаимодействие](#interaction)
10. [Layout — Раскладка](#layout)
11. [Color System — Цветовая система](#color)
12. [Typography — Типографика](#typography)
13. [Shape — Форма и морфинг](#shape)
14. [Motion — Движение (Spring Physics)](#motion)
15. [Elevation — Высота](#elevation)
16. [Iconography — Иконки](#icons)
17. [Components — Компоненты](#components)
18. [Develop — Реализация на платформах](#develop)

---

## 1. Что такое M3 Expressive {#intro}

### Философия

> "Expressive design makes you feel something. It inspires emotion, communicates function, and helps users achieve their goals."

Material 3 Expressive — это эволюция Material Design, ориентированная на эмоциональный отклик и более чёткое визуальное руководство пользователем. В отличие от обычного M3, который фокусируется на согласованности, Expressive вводит:

- Более смелые формы, цвета и размеры
- Физически обоснованную модель движения
- Морфинг форм для коммуникации состояний
- Подчёркнутые (emphasized) стили типографики

### Результаты исследований

M3 Expressive — самое исследованное обновление дизайн-системы Google за всё время:

- **46 исследований**, **18 000+ участников** по всему миру
- Пользователи замечают ключевые UI-элементы **до 4× быстрее**
- Время до тапа на основное действие сокращается на секунды
- Старшие пользователи распознают элементы наравне с молодыми
- Восприятие "современности" бренда: +34%
- Восприятие "актуальности": +32%
- Предпочтение Expressive проявляется во всех возрастных группах

### M3 Expressive vs обычный M3

| Аспект | M3 (baseline) | M3 Expressive |
|--------|---------------|---------------|
| Цель | Согласованность, нейтральность | Эмоциональная связь, ясная иерархия |
| Motion | Кривые Безье + duration | Spring physics (пружины) |
| Shape | 6 уровней скруглений | 10-шаговая шкала + 35 абстрактных форм + морфинг |
| Typography | Стандартные веса | + Emphasized стили (более жирные) |
| Colors | Стандартные роли | Те же роли, но смелее применение |
| Components | Базовый набор | + Floating Toolbar, Button Groups, Split Button, новые Loading Indicators |
| Размеры | Стандартные | Более широкие диапазоны (выше контраст между мелким/крупным) |

---

## 2. Пять фундаментальных тактик M3 Expressive {#tactics}

Google формулирует M3 Expressive через **5 строительных блоков** для направления внимания пользователя:

### 2.1 Color (Цвет)
Используй контрастные цвета для самых важных элементов. Кнопки главных действий должны "выпрыгивать" из фона за счёт акцентного цвета. Менее важные элементы — нейтральные тона. Избегай ровно распределённой палитры.

### 2.2 Shape (Форма)
Форма — инструмент коммуникации, а не только декорация. Сочетай скруглённые и угловатые элементы для создания визуального напряжения. Используй необычные формы для важных, "звёздных" элементов.

### 2.3 Size (Размер)
Делай главные элементы значительно крупнее второстепенных. Высокий контраст размеров создаёт ясную иерархию. FAB может быть очень крупным, кнопки главных действий — больше обычных.

### 2.4 Motion (Движение)
Анимация физически правдоподобна (spring-based) и привлекает внимание к изменениям. Используй морфинг форм и состояний для коммуникации, а не для декорации.

### 2.5 Containment (Контейнеры)
Группируй связанные элементы в визуально различимые контейнеры. Используй фон, бордеры, скругления для логического разделения. Самое важное — в самом выразительном контейнере.

---

## 3. Foundations — Принципы {#principles}

### Три кита Material Design 3

| Принцип | Описание |
|---------|----------|
| **Personal** | Адаптация под пользователя через Dynamic Color, акцентируется в M3 Expressive |
| **Adaptive** | Работает на всех форм-факторах: телефоны, планшеты, фолдаблы, Wear OS, XR |
| **Expressive** | Эмоциональная связь, ясная иерархия, индивидуальность бренда |

### Дизайн с намерением

M3 Expressive — это не "украшение" поверх M3. Это набор инструментов, которые применяются **избирательно**, не везде:

- Не каждая кнопка должна быть выразительной — только главная
- Не каждый заголовок — Emphasized, только ключевые моменты
- Декоративные формы — только для не-интерактивных элементов
- Морфинг — для коммуникации состояний, не как украшение

### Когда НЕ использовать Expressive подход

Expressive design подходит не везде:

- Банковские интерфейсы — серьёзный тон, минимум "игривости"
- Корпоративные dashboard — плотность данных важнее эмоций
- Утилитарные тулы — функция превыше формы

Соблюдай устоявшиеся UI-паттерны там, где они есть.

---

## 4. Adaptive Design — Адаптивный дизайн {#adaptive}

### Window Size Classes

M3 классифицирует устройства по ширине окна:

| Класс | Ширина | Устройства |
|-------|--------|-----------|
| Compact | < 600dp | Телефоны portrait |
| Medium | 600–840dp | Малые планшеты, телефоны landscape, фолдаблы внутренний экран |
| Expanded | 840–1200dp | Планшеты, складные большие |
| Large | 1200–1600dp | Десктопы, большие планшеты |
| Extra-large | > 1600dp | Большие десктопы, мониторы |

### Canonical Layouts — три канонических раскладки

#### List-Detail
Список элементов + детальный просмотр выбранного.

```
Compact:   [List] → tap → [Detail] (full screen, push navigation)
Medium+:   [List | Detail] (рядом, 360dp | flex)
```

Применение: почта, мессенджеры, файловые менеджеры, каталоги.

#### Supporting Pane
Основной контент + вспомогательная панель.

```
Compact:   [Main] → bottom sheet или modal с Pane
Medium:    [Main | Side sheet]
Expanded:  [Main | Pane] (постоянная панель, ~60/40)
```

Применение: документы со списком комментариев, чат с информацией о собеседнике.

#### Feed
Лента карточек.

```
Compact:   1 колонка
Medium:    2 колонки
Expanded:  3+ колонок (или masonry)
```

Применение: социальные ленты, галереи, новостные приложения.

### Pane Layouts — управление панелями

M3 рекомендует думать о раскладке как о **наборе панелей** (panes), которые показываются/скрываются/перестраиваются в зависимости от размера окна.

**Правила:**
- Compact: одна панель за раз, навигация через push
- Medium: две панели рядом, или одна + overlay
- Expanded+: три и больше панелей одновременно

### Адаптивная навигация

| Класс окна | Компонент навигации |
|-----------|---------------------|
| Compact | Navigation Bar (внизу), 3–5 пунктов |
| Medium | Navigation Rail (слева), 3–7 пунктов |
| Expanded | Navigation Rail (расширенный, wide) или Navigation Drawer |
| Large+ | Navigation Drawer (постоянный) |

---

## 5. Building for All — Доступность {#accessibility}

### User Needs — учёт потребностей

Material design учитывает разнообразие пользователей:

- Возрастные различия (зрение, моторика)
- Когнитивные особенности
- Временные ограничения (одна рука, шум, солнце)
- Постоянные особенности (слабовидящие, низкая моторика)
- Ситуационные ограничения (за рулём, в перчатках)

M3 Expressive специально проверялся с пользователями разных возрастов — более крупные элементы и высокий контраст показали улучшения для всех групп.

### Co-design — соавторство

Принцип: проектируй **вместе с** теми, для кого делаешь, а не **для** них.

- Тестируй на ранних этапах с реальными пользователями
- Включай в команду людей с инвалидностью
- Не угадывай потребности — спрашивай

### Контрастность (WCAG)

| Тип контента | Минимум |
|--------------|---------|
| Обычный текст | 4.5:1 (AA) |
| Крупный текст (≥ 18pt или ≥ 14pt bold) | 3:1 (AA) |
| UI-элементы (бордеры, иконки) | 3:1 |
| AAA-уровень | 7:1 |

M3 цветовые пары (primary/on-primary и т.д.) спроектированы так, чтобы соблюдать минимум 4.5:1.

### Touch targets

- Минимум **48×48dp** для всех интерактивных элементов
- Если визуально меньше — расширяй невидимую зону касания

### Focus и keyboard

- Все интерактивные элементы должны иметь видимый focus indicator
- Порядок Tab соответствует визуальному порядку
- В модалках — focus trap, при закрытии возврат фокуса к триггеру

### Не только цвет

Никогда не передавай информацию **только** цветом. Дублируй иконкой, текстом, формой.

---

## 6. Content Design — Контент {#content}

### UX Writing — лучшие практики

1. **Будь кратким.** Каждое слово оплачено вниманием пользователя.
2. **Будь ясным.** Используй простые слова, избегай жаргона.
3. **Будь полезным.** Пиши то, что помогает действовать.
4. **Будь человеческим.** Тон — дружелюбный, не формальный.

### Word Choice — выбор слов

**Хорошо:**
- Активный залог: "Удалить файл" вместо "Файл будет удалён"
- Глагол в начале кнопок: "Сохранить", "Отправить"
- Конкретика: "3 новых сообщения" вместо "Несколько уведомлений"

**Плохо:**
- Жаргон: "конфигурация" вместо "настройки"
- Двойные отрицания
- Угрожающие формулировки: "Внимание!", "Ошибка!"
- Морализаторство: "Не забудьте..."

### Grammar & Punctuation

- **Sentence case** для всех заголовков и кнопок (не TITLE CASE и не ВСЁ_БОЛЬШИМИ)
- Точки в конце предложений (даже в кратких UI-сообщениях, если предложение полное)
- Запятые по правилам грамматики
- Без восклицательных знаков, кроме редких случаев искренней позитивной эмоции

### Alt text для изображений

- Описывай **функцию**, а не внешний вид: "Кнопка отправки" вместо "Бумажный самолётик"
- Декоративные изображения помечай как `role="presentation"` или `alt=""`
- Для информативных — описывай ключевую информацию

### Global writing — локализация

- Не используй идиомы, специфичные для языка
- Учитывай разные направления письма (RTL для арабского, иврита)
- Даты, числа, валюты — через локализованные форматтеры
- Оставляй запас места: переводы могут быть на 30–50% длиннее

### Notifications — уведомления

Структура хорошего уведомления:
1. **Title** — что произошло (1 строка)
2. **Body** — детали (1–2 строки)
3. **Action(s)** — что можно сделать (опционально)

Принципы:
- Уведомляй только о важном
- Группируй похожие
- Давай возможность отписаться от типа
- Указывай источник (приложение, отправитель)

---

## 7. Customization — Кастомизация {#customization}

M3 спроектирован для глубокой кастомизации. Точки настройки:

1. **Color seed** — стартовый цвет, из которого генерируются палитры
2. **Color scheme variant** — стиль генерации (Tonal, Vibrant, Expressive, Content, и т.д.)
3. **Typography** — шрифты для Display, Headline, Title, Body, Label по отдельности
4. **Shape** — corner radius для каждой "семьи" компонентов
5. **Motion** — кастомные spring-параметры

Метод кастомизации — переопределение **design tokens** в темах.

---

## 8. Design Tokens — Токены {#tokens}

### Что такое токены

Дизайн-токен — именованная переменная, которая хранит значение стиля (цвет, размер, скругление, длительность). Это абстракция: вместо `#6750A4` ты используешь `md.sys.color.primary`.

### Трёхуровневая иерархия

```
Reference tokens  →  System tokens  →  Component tokens
       ↓                  ↓                   ↓
   Сырое значение    Семантическая роль   Конкретный компонент
   md.ref.palette    md.sys.color         md.comp.filled-button
   .primary40        .primary             .container-color
   = #6750A4         = ref.palette.       = sys.color.primary
                       primary40
```

**Reference tokens** — палитры тонов, исходные значения. Меняются редко.

**System tokens** — семантические алиасы: `primary`, `on-surface`, `outline`. Используются в коде везде.

**Component tokens** — токены конкретных компонентов: `filled-button.container-color`. Позволяют переопределить один компонент без влияния на остальные.

### Преимущества подхода

- Смена темы — изменение значений токенов, без правок UI
- Dynamic Color работает за счёт замены reference tokens на основе обоев
- Светлая/тёмная тема — два набора значений для одних имён
- Бренд-кастомизация — переопределение в одной точке
- Согласованность design ↔ code

### Использование на платформах

| Платформа | Реализация токенов |
|-----------|--------------------|
| Web (CSS) | CSS custom properties: `--md-sys-color-primary` |
| Android (Compose) | `MaterialTheme.colorScheme.primary` |
| Android (XML) | `?attr/colorPrimary` |
| Flutter | `Theme.of(context).colorScheme.primary` |
| iOS | Кастомные обёртки или Material Components |
| Figma | Color Styles + Variables |

---

## 9. Interaction — Взаимодействие {#interaction}

### States — состояния

Каждый интерактивный компонент имеет до 9 состояний:

| Состояние | Описание | State layer opacity |
|-----------|----------|---------------------|
| Enabled | Обычное | 0% |
| Disabled | Недоступно | контент 38%, контейнер 12% |
| Hovered | Курсор сверху | +8% |
| Focused | Фокус клавиатуры | +12% + focus ring |
| Pressed | Нажато | +12% + ripple |
| Dragged | Перетаскивается | +16% |
| Selected | Выбрано | смена цвета контейнера |
| Activated | Активный пункт навигации | смена цвета |
| Error | Ошибка | error-color overlay |

**State layer** — полупрозрачный слой поверх компонента, который добавляется при состояниях. Цвет — `currentColor` (или semantic).

### Gestures — жесты

M3 поддерживает стандартные жесты:

| Жест | Действие |
|------|----------|
| Tap | Активация |
| Long press | Контекстное меню, выделение |
| Drag | Перемещение, сортировка |
| Swipe | Действие на элементе списка, навигация |
| Pinch | Zoom |
| Pull-to-refresh | Обновление контента |
| Scroll | Прокрутка |

### Inputs — методы ввода

M3 проектируется для **любого метода ввода**:
- Touch (палец)
- Stylus (перо)
- Mouse + Keyboard
- Game controller (для XR, Android TV)
- Voice
- Accessibility services (TalkBack, VoiceOver)

Не предполагай только touch.

### Selection — выбор

Паттерны выбора:
- **Single select** — radio, dropdown, segmented button
- **Multi select** — checkbox, filter chips, multi-select list
- **Range select** — slider, date range picker

Правило: всегда показывай **текущий** выбор визуально (не только через состояние).

---

## 10. Layout — Раскладка {#layout}

### Spacing system

Базовая единица — **4dp**. Всё кратно 4, предпочтительно — 8.

| Токен | Значение | Применение |
|-------|----------|-----------|
| 4dp | минимальный gap | внутри inline-элементов |
| 8dp | стандартный gap | между компонентами |
| 12dp | средний padding | внутри chips, меньших компонентов |
| 16dp | стандартный padding | cards, list items, text fields |
| 24dp | крупный padding | dialogs, больших layouts |
| 32dp | секционный gap | разделение секций |
| 48dp | touch target | минимум для интерактивов |

### Margins и gutters

| Класс окна | Margin (по краям) | Gutter (между колонками) |
|-----------|------------------|--------------------------|
| Compact | 16dp | 16dp |
| Medium | 24dp | 24dp |
| Expanded+ | 24dp | 24dp |

### Grid

| Класс окна | Колонки |
|-----------|---------|
| Compact | 4 |
| Medium | 8 или 12 |
| Expanded | 12 |

### Safe areas

Учитывай вырезы и системные панели:

```css
padding-top: env(safe-area-inset-top);
padding-bottom: env(safe-area-inset-bottom);
padding-left: env(safe-area-inset-left);
padding-right: env(safe-area-inset-right);
```

---

## 11. Color System — Цветовая система {#color}

### Как работает система

M3 использует цветовое пространство **HCT** (Hue, Chroma, Tone) — перцептивно точное, в отличие от HSL.

- **Hue** (0–360) — оттенок
- **Chroma** (0–~120) — насыщенность (0 = серый)
- **Tone** (0–100) — светлота (0 = чёрный, 100 = белый)

Из одного seed-цвета генерируются **5 тональных палитр** по 13 тонов (0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 95, 99, 100):

- Primary
- Secondary
- Tertiary
- Neutral
- Neutral Variant
- + Error (фиксированная)

### Color Roles — роли цвета

Полный набор ролей (29 ключевых):

#### Primary семья
- `primary` — главный акцент
- `on-primary` — контент на primary
- `primary-container` — мягкий фон с акцентом
- `on-primary-container` — контент на primary-container

#### Secondary семья
- `secondary` — вторичный акцент
- `on-secondary`, `secondary-container`, `on-secondary-container`

#### Tertiary семья
- `tertiary` — контрастный третий цвет
- `on-tertiary`, `tertiary-container`, `on-tertiary-container`

#### Error семья
- `error`, `on-error`, `error-container`, `on-error-container`

#### Surface (поверхности)
- `surface` — базовая поверхность
- `surface-dim`, `surface-bright` — тёмная/яркая
- `surface-container-lowest` — самая светлая
- `surface-container-low`
- `surface-container`
- `surface-container-high`
- `surface-container-highest` — самая тёмная (в светлой теме)
- `on-surface`, `on-surface-variant`

#### Дополнительные
- `outline`, `outline-variant` — бордеры
- `inverse-surface`, `inverse-on-surface`, `inverse-primary` — для контраста (snackbar)
- `scrim`, `shadow` — затемнение, тени
- `surface-tint` — overlay для elevation

### Fixed colors (новое в M3 Expressive)

Цвета, которые **не меняются** между светлой и тёмной темой — для бренд-консистентности:

- `primary-fixed`, `on-primary-fixed`
- `primary-fixed-dim`, `on-primary-fixed-variant`
- Аналогично для secondary, tertiary

### Baseline palette (значения по умолчанию)

#### Светлая тема (ключевые)
| Роль | Hex |
|------|-----|
| primary | #6750A4 |
| on-primary | #FFFFFF |
| primary-container | #EADDFF |
| on-primary-container | #21005D |
| secondary | #625B71 |
| tertiary | #7D5260 |
| error | #B3261E |
| surface | #FEF7FF |
| on-surface | #1D1B20 |
| surface-container | #F3EDF7 |
| outline | #79747E |
| background | #FEF7FF |

#### Тёмная тема (ключевые)
| Роль | Hex |
|------|-----|
| primary | #D0BCFF |
| on-primary | #381E72 |
| primary-container | #4F378B |
| on-primary-container | #EADDFF |
| secondary | #CCC2DC |
| tertiary | #EFB8C8 |
| error | #F2B8B5 |
| surface | #141218 |
| on-surface | #E6E0E9 |
| surface-container | #211F26 |
| outline | #938F99 |

### Выбор цветовой схемы (Choosing a scheme)

M3 предлагает несколько **вариантов схем** из одного seed-цвета:

| Вариант | Характер |
|---------|---------|
| Tonal Spot | Стандартный, сбалансированный (default) |
| Neutral | Минимум насыщенности, серая база |
| Vibrant | Высокая насыщенность primary |
| Expressive | Контрастные secondary/tertiary, для M3 Expressive |
| Content | Извлекается из контента (картинки) |
| Fidelity | Точно сохраняет seed-цвет |
| Monochrome | Чёрно-белая схема |
| Rainbow | Радужные secondary/tertiary |
| Fruit Salad | Контрастные палитры |

### Источники цвета

#### Static (статичный)
- **Baseline** — стандартная палитра M3 (фиолетовая)
- **Custom brand** — на основе цвета бренда

#### Dynamic (динамический)
- **User-generated source** — из обоев устройства (Android 12+)
- **Content-based source** — из контента приложения (обложки, фото)

### Custom colors

Можно добавлять дополнительные цвета вне схемы — для брендовых акцентов, статусов (success, warning, info), категорий контента. M3 поддерживает их через extension tokens.

### Применение цветов в M3 Expressive

- Используй контрастные цвета для **главного действия** на экране
- Группы связанных элементов — одинаковый container-цвет
- Не делай "ровную" палитру: акцент должен выделяться
- Tertiary — для неожиданных, эмоциональных акцентов
- Surface containers — для логической группировки

---

## 12. Typography — Типографика {#typography}

### Шкала типографики

M3 определяет **15 базовых стилей** в 5 категориях, каждая в 3 размерах (Large/Medium/Small):

| Категория | Назначение |
|-----------|-----------|
| Display | Самые крупные — hero, marketing |
| Headline | Заголовки разделов, страниц |
| Title | Заголовки компонентов (диалоги, карточки) |
| Label | Кнопки, chips, навигация |
| Body | Основной читаемый текст |

### Размеры (sp/dp)

| Стиль | Size | Line | Weight | Tracking |
|-------|------|------|--------|----------|
| Display Large | 57 | 64 | 400 | -0.25 |
| Display Medium | 45 | 52 | 400 | 0 |
| Display Small | 36 | 44 | 400 | 0 |
| Headline Large | 32 | 40 | 400 | 0 |
| Headline Medium | 28 | 36 | 400 | 0 |
| Headline Small | 24 | 32 | 400 | 0 |
| Title Large | 22 | 28 | 400 (Regular) | 0 |
| Title Medium | 16 | 24 | 500 (Medium) | 0.15 |
| Title Small | 14 | 20 | 500 | 0.1 |
| Body Large | 16 | 24 | 400 | 0.5 |
| Body Medium | 14 | 20 | 400 | 0.25 |
| Body Small | 12 | 16 | 400 | 0.4 |
| Label Large | 14 | 20 | 500 | 0.1 |
| Label Medium | 12 | 16 | 500 | 0.5 |
| Label Small | 11 | 16 | 500 | 0.5 |

### Emphasized styles (новое в M3 Expressive)

Параллельный набор стилей с **более жирными весами** для создания визуальной иерархии:

- Те же 15 размеров
- Веса увеличены (Regular → Medium, Medium → Bold, и т.д.)
- Применяются избирательно — на ключевых моментах: hero-заголовках, выбранных пунктах, важных действиях

```
Display Large           : 400 weight
Display Large Emphasized: 500–700 weight (зависит от шрифта)

Body Large           : 400
Body Large Emphasized: 500
```

**Правило применения:**
- Display + Headline — отличные кандидаты для Emphasized (короткие, важные)
- Body — обычно остаётся в baseline (читаемость на длинных текстах)
- Label — Emphasized для главной кнопки

### Variable fonts

M3 Expressive активно использует **variable fonts** (Roboto Flex, Google Sans Flex):

Регулируемые оси:
- `wght` (weight) — 100–1000
- `wdth` (width) — 25–151
- `slnt` (slant) — наклон
- `opsz` (optical size) — оптический размер

Variable fonts позволяют **анимировать вес** для реакции на взаимодействие — кнопка становится "толще" при нажатии. Это часть expressive feedback.

### Шрифты

- **Default**: Roboto / Roboto Flex
- **Branded**: Google Sans / Google Sans Flex (на устройствах Google)
- **Custom**: любой шрифт с поддержкой нужных весов

Рекомендуется использовать максимум 2 шрифта:
- Display/Headline — выразительный, характерный
- Body — оптимизированный для читаемости

### Применение

- **Не больше 2 шрифтов** в проекте
- **Sentence case** для всех UI-элементов (не Title Case, не UPPERCASE)
- Минимальный размер для UI-текста — 12sp
- Минимальный для body — 14sp
- Достаточный line-height (1.4–1.5× размера)

---

## 13. Shape — Форма и морфинг {#shape}

### Принципы Shape в M3 Expressive

> Shape can direct attention, communicate state, and express brand.

Форма — не декорация, а инструмент:
1. **Направление внимания** — необычная форма выделяет важное
2. **Коммуникация состояния** — морфинг показывает изменение
3. **Брендинг** — характерная форма закрепляется в памяти

### Corner Radius Scale (расширенная в M3 Expressive)

Расширена с 6 до **10+ ступеней**:

| Token | Value | Применение |
|-------|-------|-----------|
| `corner.none` | 0dp | Без скруглений |
| `corner.extra-small` | 4dp | Меню, snackbar, tooltip |
| `corner.small` | 8dp | Chips, text fields |
| `corner.medium` | 12dp | Cards, диалоги (старая база) |
| `corner.large` | 16dp | FAB, navigation drawer |
| `corner.large-increased` | 20dp | (новое) |
| `corner.extra-large` | 28dp | Диалоги, bottom sheets |
| `corner.extra-large-increased` | 32dp | (новое) |
| `corner.extra-extra-large` | 48dp | (новое) Очень крупные блоки |
| `corner.full` | 9999px (50% или полный) | Pills, круги |

**Изменение в M3 Expressive**: `corner.full` теперь означает **полностью скруглённый** (pill-shape), а не "50% от размера". Это даёт стабильное визуальное поведение независимо от размера компонента.

### Asymmetric corners (асимметричные скругления)

Каждый угол можно настраивать отдельно:
- `corner.large-top` — только верхние углы (bottom sheets)
- `corner.large-start` — только стартовые (LTR: левые)
- Любые комбинации через 4 значения

### Шаблоны форм (35 новых форм в M3 Expressive)

Material Shapes Library (Figma + Compose) добавляет **35 абстрактных форм** помимо прямоугольников:

Категории форм:
- **Pill/Circle** — таблеточные, круглые
- **Cookie** — волнистые, "печеньки"
- **Pixel** — пиксельные, ступенчатые
- **Diamond** — ромбовидные
- **Burst** — с лучами, "взрывы"
- **Clover** — лопасти, клевер
- **Heart** — сердца
- **Sunny** — солнечные с лучами
- **Triangle** — треугольники
- **Square** — модифицированные квадраты
- **Arch** — арки, полукруги
- **Boom** — со звёздочками, искрами
- **Flower** — цветочные

**Применение:**
- Аватары необычной формы
- Декоративные обрезки фото
- Loading indicators (морфят между формами)
- Иконки-контейнеры
- Bullet points в стиле бренда

### Shape Morphing (морфинг форм)

> Built-in shape morphing — smooth animated transitions from one shape to another.

Ключевая фича M3 Expressive: формы могут **плавно превращаться** одна в другую.

Применения:
- **State indication**: квадратная кнопка → круглая при выборе
- **Loading indicators**: морфинг между разными формами вместо вращающегося круга
- **Carousel cards**: разные формы для разных позиций
- **Time/progress**: волнообразная форма меняется с прогрессом
- **Selection feedback**: pill-chip увеличивает border-radius при выборе

### Принципы применения

**Use shape with intent (используй форму осознанно):**
- Выбирай форму, которая поддерживает функцию
- Не используй абстрактные формы для интерактивов — они должны читаться как "что-то нажимаемое"

**Use abstract shapes sparingly (абстрактные формы — экономно):**
- Только для декоративных, неинтерактивных элементов
- Креативный crop фото, маски аватаров

**Create visual tension (создавай визуальное напряжение):**
- Сочетай скруглённое и угловатое
- Контраст форм направляет взгляд

**Emphasize aesthetic moments (подчёркивай эстетические моменты):**
- Hero-секции, welcome-экраны — место для smelых форм
- Обычные списки и формы — простые скругления

---

## 14. Motion — Движение (Spring Physics) {#motion}

### Motion Physics System — новая система

M3 Expressive вводит **физическую модель движения** на основе пружин — это замена системе easing + duration.

> The physics system is replacing the previous system based on easing and duration.

### Принципы

1. **Natural** — движение следует законам физики
2. **Responsive** — реагирует на действия пользователя (драг, флинг)
3. **Energetic** — живое, не вялое
4. **Purposeful** — несёт смысл, не декоративное

### Spring Tokens

Два типа пружин:

#### Spatial — для перемещения
Объекты, которые движутся в пространстве. Может быть лёгкий overshoot (перелёт за цель и возврат), как у реальной пружины.

| Token | Stiffness | Damping | Применение |
|-------|-----------|---------|-----------|
| `spatial.default` | 700 | 0.9 | Обычные перемещения |
| `spatial.fast` | 1400 | 0.9 | Быстрые, малые движения |
| `spatial.slow` | 350 | 0.9 | Крупные, важные переходы |

#### Effect — для эффектов
Цвет, прозрачность, blur — overshoot нежелателен. Высокое демпфирование.

| Token | Stiffness | Damping | Применение |
|-------|-----------|---------|-----------|
| `effect.default` | 1600 | 1.0 | Fade, color, opacity |
| `effect.fast` | 3800 | 1.0 | Мгновенные эффекты |
| `effect.slow` | 800 | 1.0 | Плавные эффекты |

### Старые токены (Easing + Duration) — для обратной совместимости

Используются там, где spring не нужен или для не-моушн анимаций (CSS transitions):

#### Easing curves
| Token | Curve |
|-------|-------|
| `emphasized` | cubic-bezier(0.2, 0, 0, 1.0) |
| `emphasized-decelerate` | cubic-bezier(0.05, 0.7, 0.1, 1.0) |
| `emphasized-accelerate` | cubic-bezier(0.3, 0, 0.8, 0.15) |
| `standard` | cubic-bezier(0.2, 0, 0, 1.0) |
| `standard-decelerate` | cubic-bezier(0, 0, 0, 1) |
| `standard-accelerate` | cubic-bezier(0.3, 0, 1, 1) |
| `legacy` | cubic-bezier(0.4, 0, 0.2, 1) |

#### Duration tokens
| Group | Values |
|-------|--------|
| Short | 50, 100, 150, 200ms |
| Medium | 250, 300, 350, 400ms |
| Long | 450, 500, 550, 600ms |
| Extra-long | 700, 800, 900, 1000ms |

### Transition Patterns — паттерны переходов

#### 1. Container Transform
Элемент трансформируется в другой (карточка → деталь). Spring spatial.slow.

#### 2. Shared Axis (X / Y / Z)
Переход с общей осью движения. Используется для иерархических переходов:
- **X** — вперёд/назад в иерархии (drill-in / pop-back)
- **Y** — то же, но вертикально (раскрытие, аккордеоны)
- **Z** — глубина (модалки, диалоги)

#### 3. Fade Through
Старый контент уходит fade-out → новый fade-in. Для несвязанных переходов (вкладки).

#### 4. Fade
Простое появление/исчезновение. Effect spring.

### Spring motion в реальных интерактивах

Примеры из Android 16 / M3 Expressive:

- **Dismiss notification** — соседние уведомления "пружинят" в ответ на драг, последнее даёт haptic feedback при отрыве
- **Volume slider** — thumb упруго следует за пальцем
- **Recents screen** — флинг карточки имеет инерцию и pruzhinit при выходе
- **Quick Settings** — расширение тайла с pружинной анимацией

### Reduced motion

Уважай системную настройку `prefers-reduced-motion`:
- Заменяй spring/spatial анимации на простой fade
- Сокращай duration до 50-100ms
- Не убирай движение полностью — пользователь может потерять контекст

---

## 15. Elevation — Высота {#elevation}

### Принцип

В M3 elevation выражается **двумя способами**:
1. **Shadow** — тень (как раньше)
2. **Tonal surface** — изменение оттенка поверхности через смешивание с primary

В тёмной теме тени малозаметны — оттенок становится главным индикатором elevation.

### Уровни elevation

| Level | Height | Применение |
|-------|--------|-----------|
| 0 | 0dp | Фон, navigation bar (нескроллированный) |
| 1 | 1dp | Cards, app bar (скроллированный), assist chip |
| 2 | 3dp | Menus, tooltips, search bar (поднятый) |
| 3 | 6dp | FAB, modal bottom sheet |
| 4 | 8dp | (редко) FAB pressed, Navigation drawer modal |
| 5 | 12dp | (очень редко) |

### Tonal overlay opacities

В тёмной теме (и опционально в светлой) на surface накладывается primary с прозрачностью:

| Level | Opacity |
|-------|---------|
| 0 | 0% |
| 1 | 5% |
| 2 | 8% |
| 3 | 11% |
| 4 | 12% |
| 5 | 14% |

Это даёт постепенно более светлые поверхности с ростом elevation в тёмной теме.

### Применение

- Не злоупотребляй: 3+ уровней elevation на одном экране — путаница
- Чем выше level, тем "важнее" элемент должен быть
- При hover/press можно повышать level на 1
- Для группировки используй surface-containers (level 0), не тени

---

## 16. Iconography — Иконки {#icons}

### Material Symbols

Современная библиотека иконок Google — **Material Symbols** (вариативный шрифт).

Доступна в 3 стилях:
- **Outlined** — контурные (default)
- **Rounded** — со скруглёнными концами
- **Sharp** — с острыми углами

### Variable axes

Material Symbols — variable font с осями:

| Axis | Range | Default | Что меняет |
|------|-------|---------|------------|
| `FILL` | 0–1 | 0 | 0 = outline, 1 = filled |
| `wght` | 100–700 | 400 | Толщина линий |
| `GRAD` | -25–200 | 0 | Визуальный вес |
| `opsz` | 20–48 | 24 | Оптический размер |

**Анимация осей**: можно плавно менять FILL от 0 до 1 при выделении — иконка "заполняется". Это естественный feedback в M3 Expressive.

### Designing icons

Принципы создания собственных иконок:

- **Pixel grid**: 24×24dp (стандарт), 20, 40, 48
- **Padding**: 2dp от края до иконки (для 24dp)
- **Stroke**: 2dp для outlined
- **Corner radius**: совпадает с языком приложения
- **Centering**: оптический центр, не геометрический

### Стандартные размеры

| Назначение | Размер |
|-----------|--------|
| Navigation icons | 24dp |
| FAB icons | 24dp |
| Button icons | 18dp |
| List icons | 24dp |
| App icons (launcher) | 48dp+ |

---

## 17. Components — Компоненты {#components}

> M3 Expressive обновляет существующие компоненты и добавляет новые. Все компоненты поддерживают tokens, новые формы, emphasized typography, spring motion.

### Кнопки (Buttons)

5 видов кнопок (расширены в M3 Expressive):

| Тип | Применение | Высота (Expressive: больше вариантов) |
|-----|-----------|----------------------------------------|
| Filled | Главное действие | 40dp (стандарт), новые: XS 32dp, S 40dp, M 56dp, L 96dp, XL 136dp |
| Elevated | Заметная альтернатива на сложном фоне | Те же размеры |
| Filled Tonal | Средний приоритет | Те же размеры |
| Outlined | Альтернативное действие | Те же размеры |
| Text | Низкий приоритет | Те же размеры |

**Изменения в M3 Expressive:**
- 5 размеров кнопок (XS — XL) вместо одного 40dp
- Корнеры по умолчанию увеличены, можно делать pill (full)
- Поддержка morphing: квадрат → pill при состоянии
- Emphasized label стиль для Filled кнопок

### Button Groups (новое в M3 Expressive)

Контейнер для группы кнопок (или icon-кнопок) с динамическим поведением:
- При наведении/нажатии на одну — соседние "сжимаются"
- Поддерживают разные формы внутри группы
- Применение: панели инструментов, segmented controls

### Split Button (новое в M3 Expressive)

Кнопка с двумя зонами:
- Левая — основное действие
- Правая — раскрывающийся список альтернатив (dropdown)

Применение: "Сохранить" + "Сохранить как..." / "Сохранить копию"

### FAB (Floating Action Button)

Размеры:
| Variant | Size |
|---------|------|
| FAB small | 40dp |
| FAB (regular) | 56dp |
| FAB large | 96dp |
| Extended FAB | 56dp h × variable w |

**В M3 Expressive:**
- FAB Menu — раскрывающийся набор действий из FAB
- Toolbar FAB — FAB, встроенный в floating toolbar

### Floating Toolbar (новое в M3 Expressive)

Pill-образная панель с действиями, плавающая над контентом:
- Не растягивается на всю ширину
- Edge-to-edge дизайн с просветами по бокам
- Содержит icon buttons, разделители
- Опционально содержит FAB

Применение: контекстные действия (форматирование текста, действия на выделении).

### Cards

3 вида:
- **Elevated** — тень + surface-container-low
- **Filled** — surface-container-highest
- **Outlined** — outline-variant бордер

Shape: `corner.medium` (12dp) по умолчанию, в M3 Expressive часто увеличивается до `corner.large` (16dp).

### Chips

4 вида:
- **Assist** — умные действия
- **Filter** — фильтрация контента (multi-select)
- **Input** — теги
- **Suggestion** — single-select предложения

В M3 Expressive: chips могут менять форму (morph) при выборе.

### Text Fields

2 вида:
- **Filled** — surface fill + bottom line
- **Outlined** — border outline

Высота: 56dp.

### Dialogs

- Shape: `corner.extra-large` (28dp)
- Max width: 560dp
- Padding: 24dp
- 2 действия максимум (cancel + confirm)

### Bottom Sheets

- Standard — не блокирует контент
- Modal — со scrim
- Shape: `corner.extra-large` сверху

### Navigation Bar

- 80dp высота
- 3–5 destinations
- Активный — filled icon + indicator pill (secondary-container)
- Compact layout

### Navigation Rail

- Compact (80dp wide) — иконка + label вертикально
- **Wide (новое в M3 Expressive)** — 220dp+, иконка + label горизонтально

### Navigation Drawer

- Standard (постоянный, 256–360dp)
- Modal (выезжающий)

### Top App Bar

4 варианта:
- Center-aligned (64dp)
- Small (64dp)
- Medium (112dp, collapsing)
- Large (152dp, collapsing)

### Snackbar

- Shape: `corner.extra-small` (4dp)
- Inverse-surface background
- 1 опциональное action

### Tabs

- Primary (с индикатором)
- Secondary (без полоски, для контента)

### Loading Indicators (обновлены в M3 Expressive)

Новые компоненты:
- **LoadingIndicator** — морфит между абстрактными формами (вместо вращающегося круга), для коротких ожиданий (< 5 сек)
- **ContainedLoadingIndicator** — то же, но внутри цветного контейнера

Старые продолжают работать:
- `LinearProgressIndicator`
- `CircularProgressIndicator` (рекомендуется только для определённого прогресса)

### Switches, Checkboxes, Radio

В M3 Expressive — увеличенные thumb-размеры, морфинг иконки внутри switch.

### Sliders

- Track: 4dp
- Thumb: 20dp + 40dp state layer
- **Expressive обновление**: thumb может менять форму/размер при перетаскивании

### Date/Time Pickers

- Shape: `corner.extra-large` (28dp)
- Поддержка input/picker mode
- Range selection

### Menus, Tooltips

- Menu: `corner.extra-small` (4dp), elevation 2
- Tooltip: inverse-surface, `corner.extra-small`

### Lists

- One-line: 56dp
- Two-line: 72dp
- Three-line: 88dp

### Carousels (обновлено в M3 Expressive)

Карусели карточек с динамической формой:
- Центральная карточка — крупнее, особая форма
- Боковые — меньше, простая форма
- При скролле — морфинг размеров и форм

### Search

- **Search bar** — заменяет top app bar
- **Search view** — раскрывается из icon

Shape: `corner.full` (pill).

### Bottom App Bar

- Высота 80dp
- Содержит icon buttons + опционально FAB

### Badges

- Small (без текста): 6dp
- Large (с текстом): 16dp height

### Wear OS специфичные (M3 Expressive)

Для круглых экранов:
- **Edge-hugging buttons** — кнопки, повторяющие кривизну дисплея
- **Round-edge containers** — контейнеры, максимизирующие пространство в круге
- **Shape-morphing lists** — элементы списка меняют форму на краях экрана

---

## 18. Develop — Реализация на платформах {#develop}

### Android — Jetpack Compose (рекомендуется)

Compose **полностью поддерживает M3 Expressive**.

```kotlin
// build.gradle
implementation("androidx.compose.material3:material3:<latest>")
implementation("androidx.compose.material3:material3-adaptive:<latest>")

// Для expressive компонентов требуется opt-in:
@OptIn(ExperimentalMaterial3ExpressiveApi::class)
```

Использование:
```kotlin
MaterialTheme(
    colorScheme = dynamicLightColorScheme(LocalContext.current),
    typography = Typography,
    shapes = Shapes
) {
    Button(
        onClick = { },
        // M3 Expressive: 5 размеров
    ) {
        Text("Save")
    }
}
```

Compose поддерживает:
- Все expressive компоненты (с @OptIn)
- Dynamic Color (Android 12+)
- Spring motion через `animateFloatAsState` + spring spec
- Shape morphing через `MaterialShapes` API
- Adaptive layouts через `material3-adaptive`

### Android — Views (MDC-Android)

Material Components for Android (MDC) — классический Views-based подход.
- Поддерживает M3, частично M3 Expressive
- Менее активная разработка по сравнению с Compose
- Используй для существующих проектов или там, где Compose невозможен

```xml
<style name="Theme.MyApp" parent="Theme.Material3.DayNight">
    <item name="colorPrimary">@color/md_theme_primary</item>
    ...
</style>
```

### Flutter

Flutter поддерживает базовый M3 (`useMaterial3: true` в `ThemeData`), но **на момент 2025 года не реализует M3 Expressive**.

```dart
MaterialApp(
  theme: ThemeData(
    useMaterial3: true,
    colorSchemeSeed: Colors.purple,
    brightness: Brightness.light,
  ),
);
```

Команда Flutter сознательно не торопится с M3 Expressive — учитывают опыт миграции M2 → M3. PRs от сообщества по Expressive компонентам не принимаются.

### Web — Material Web Components

`@material/web` — веб-компоненты от Google.
- Поддерживает M3 (color, typography, shape, базовые компоненты)
- **В режиме поддержки** (maintenance mode) — без новых компонентов M3 Expressive
- Для React/Vue/Angular — потребуется обёртка

```html
<script type="module" src="https://esm.run/@material/web/all.js"></script>
<md-filled-button>Click me</md-filled-button>
```

Альтернативы для веба:
- **MUI (Material UI)** — React. M3 поддержка пока в работе (планировалась к концу 2024, отложена)
- Кастомная реализация через CSS токены + любой UI-фреймворк

### iOS / SwiftUI

Официальной библиотеки Material для iOS нет (старая MDC-iOS заморожена).
- Используй **дизайн-токены** для согласованности с другими платформами
- Реализуй компоненты на нативном SwiftUI с применением M3 спецификаций

### Кросс-платформа: общий подход

1. Определи дизайн-токены **один раз** (Tokens Studio, Style Dictionary)
2. Экспортируй в платформо-специфичные форматы:
   - iOS: Swift constants
   - Android: XML / Compose
   - Web: CSS custom properties / JS
   - Figma: Variables
3. Реализуй компоненты на каждой платформе с использованием токенов

### Get Started — путь внедрения

1. **Определи тему**: seed-цвет, шрифты, основные формы
2. **Сгенерируй палитру** через [Material Theme Builder](https://material-foundation.github.io/material-theme-builder/)
3. **Установи библиотеку** для платформы
4. **Замени старые компоненты** на M3 постепенно
5. **Применяй expressive тактики** на ключевых экранах (главные кнопки, hero, состояния)
6. **Тестируй** на разных размерах окон и устройств
7. **Проверь доступность** (контраст, touch targets, screen reader)

---

## Глоссарий

| Термин | Определение |
|--------|-------------|
| **M3 Expressive** | Текущая итерация Material Design (2025+), расширение M3 |
| **Design Token** | Именованная переменная стиля (цвет, размер, и т.д.) |
| **HCT** | Цветовое пространство Hue-Chroma-Tone, основа M3 |
| **Dynamic Color** | Генерация цветовой схемы из источника (обои, контент) |
| **Material You** | Маркетинговое название M3 с акцентом на персонализацию |
| **State Layer** | Полупрозрачный overlay для состояний (hover, press, focus) |
| **Tonal Palette** | Набор из 13 тонов одного оттенка (0–100) |
| **Surface Tint** | Primary-цвет, смешиваемый с поверхностью для elevation в dark theme |
| **Shape Morphing** | Анимированный переход формы из одной в другую |
| **Spring Spec** | Параметры пружины: stiffness + damping (вместо easing+duration) |
| **Emphasized Type** | Параллельные стили типографики с увеличенным весом |
| **Variable Font** | Шрифт с регулируемыми осями (вес, ширина, наклон) |
| **Window Size Class** | Категория размера окна (compact/medium/expanded) |
| **Canonical Layout** | Стандартный паттерн раскладки (List-Detail, Supporting Pane, Feed) |
| **Pane** | Логическая часть раскладки, может появляться/скрываться адаптивно |

---

## Ресурсы

- **Главный сайт**: https://m3.material.io
- **Get Started**: https://m3.material.io/get-started
- **M3 Expressive blog**: https://m3.material.io/blog/building-with-m3-expressive
- **Theme Builder**: https://material-foundation.github.io/material-theme-builder/
- **Figma Kit**: Material 3 Design Kit (Figma Community)
- **Material Shapes Library**: в Figma и Compose
- **Material Symbols**: https://fonts.google.com/icons
- **Compose Material 3**: https://developer.android.com/jetpack/compose/designsystems/material3
- **Compose Material 3 Adaptive**: https://developer.android.com/develop/ui/compose/layouts/adaptive
- **Material Web**: https://github.com/material-components/material-web
- **Google Design (research)**: https://design.google/library
