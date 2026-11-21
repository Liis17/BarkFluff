# Material 3 Expressive — руководство по дизайну приложений

> Сверено с Material Design 3 **21 ноября 2026**. Expressive развивает M3 с мая 2025 года. В 2026 году Material добавил Expressive Layout Scaffold, систему отступов и новые варианты компонентов. Доступность реализации проверяй по платформе и версии библиотеки. [Обзор Expressive](https://m3.material.io/blog/building-with-m3-expressive) · [Обновления Google I/O 2026](https://m3.material.io/blog/whats-new-at-io26)

---

## Содержание

1. [Что такое M3 Expressive](#intro)
2. [Тактики Expressive](#tactics)
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
19. [Применение в BarkFluff](#barkfluff)
20. [Карта официальных страниц](#source-map)

---

<a name="intro"></a>

## 1. Что такое M3 Expressive

### Философия

Material 3 Expressive развивает визуальную иерархию и помогает людям быстрее находить важные элементы. Для этого система предлагает:

- Более смелые формы, цвета и размеры
- Физически обоснованную модель движения
- Морфинг форм для коммуникации состояний
- Подчёркнутые (emphasized) стили типографики

### Результаты исследований

Google провёл **46 исследований** с участием **более 18 000 человек**. В тестовых сценариях участники замечали ключевые элементы на Expressive-экранах **до четырёх раз быстрее**. Предпочтение таким экранам наблюдалось в разных возрастных группах. Проверяй, удаётся ли в твоём продукте быстро увидеть основное действие, распознать состояние и завершить сценарий. [Результаты исследований Material](https://m3.material.io/blog/building-with-m3-expressive)

### Что добавилось

В первом обновлении Expressive было 14 новых или обновлённых компонентов: App bars, Button groups, Buttons, Extended FAB, FAB menu, FAB, Icon buttons, Loading indicator, Navigation bar, Navigation rail, Progress indicators, Sliders, Split button и Toolbars. Позже появились и другие Expressive-варианты. Для планирования реализации проверяй страницу компонента и версию библиотеки. [Состав обновления 2025](https://m3.material.io/blog/building-with-m3-expressive) · [Обновления 2026](https://m3.material.io/blog/whats-new-at-io26)

---

<a name="tactics"></a>

## 2. Тактики Material 3 Expressive

| Приём | Применение |
|---|---|
| Форма | Контраст округлых и угловатых элементов выделяет главное действие или выбранное состояние. |
| Цвет | Различимые роли и тона показывают приоритет, связь и состояние. |
| Масштаб и типографика | Размер и emphasized-начертание направляют внимание на важное действие, заголовок или значение. |
| Группировка | Отступы, поверхность и границы объединяют относящиеся друг к другу элементы. |
| Движение | Пружины и морфинг помогают понять, куда переместился объект и какое состояние изменилось. |
| Гибкость компонентов | Меняй размер, вариант или положение контрола по условиям ввода и доступному пространству. |
| Сочетание приёмов | Выделяй один или два главных момента продукта. Выбирай приёмы, которые помогают завершить конкретную задачу. |

В примере Aura команда Material сочетает масштаб, цвет и группировку для старта основной задачи, а в ходе выполнения оставляет пространство и меньше действий для сохранения фокуса. Применяй такой же ход рассуждения к своим сценариям и проверяй, как люди находят главное действие, возвращаются к задаче и восстанавливаются после ошибки. [Expressive tactics](https://m3.material.io/blog/building-with-m3-expressive) · [Usability](https://m3.material.io/foundations/usability/overview) · [Разбор Aura](https://m3.material.io/foundations/usability/applying-m-3-expressive)

---

<a name="principles"></a>

## 3. Foundations — Принципы

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

### Выразительность в контексте задачи

Выбирай степень акцента по задаче и частоте взаимодействия. На экране приветствия уместны крупный заголовок и фигурная обрезка изображения. В плотном списке чатов полезнее выделить непрочитанное начертанием и сохранить стабильную геометрию строк. Для действий с серьёзными последствиями используй ясный текст и предсказуемое поведение. [Принципы Material](https://m3.material.io/foundations/overview/principles) · [Expressive tactics](https://m3.material.io/blog/building-with-m3-expressive)

---

<a name="adaptive"></a>

## 4. Adaptive Design — Адаптивный дизайн

### Breakpoints и Expressive Layout Scaffold

Выбирай компоновку по доступной ширине окна. Размер может измениться при повороте, складывании устройства, смене размера окна или переходе в многооконный режим. При малой высоте дополнительно проверь, помещаются ли заголовок, основной контент, закреплённые действия и экранная клавиатура.

| Breakpoint | Ширина окна | Видимые панели | Навигация |
|---|---|---|---|
| Compact | < 600 dp | 1 | Navigation bar или модальный expanded navigation rail |
| Medium | 600–839 dp | 1 по умолчанию, 2 при достаточном месте | Navigation bar или модальный expanded navigation rail |
| Expanded | 840–1199 dp | Обычно 2 | Expanded navigation rail, стандартный или модальный |
| Large | 1200–1599 dp | Обычно 2 | Expanded navigation rail, стандартный или модальный |
| Extra-large | ≥ 1600 dp | Обычно 2, возможно 3 | Expanded navigation rail, стандартный или модальный |

При переходе между порогами реши, какие элементы показать, разделить, изменить в размере, переместить или заменить. Сохраняй выбранный объект, фильтры, позицию прокрутки и ввод. [Breakpoints](https://m3.material.io/foundations/layout/breakpoints/overview) · [Panes](https://m3.material.io/foundations/layout/scaffold/panes)

Scaffold организует экран в три области. **Bars** располагаются сверху или снизу и содержат заголовок, действия или навигацию. **Rails** занимают боковой край. **Panes** содержат основной и связанный контент. Оставляй хотя бы одну панель гибкой по ширине. Дополнительную панель можно показать или скрыть, наложить поверх основной (*levitate*) либо перераспределить между ними пространство (*reflow*). Если доступно перетаскивание для изменения размера, дай клавиатурный способ выполнить то же действие. [Scaffold](https://m3.material.io/foundations/layout/scaffold/overview) · [Bars](https://m3.material.io/foundations/layout/scaffold/bars) · [Rails](https://m3.material.io/foundations/layout/scaffold/rails) · [Panes](https://m3.material.io/foundations/layout/scaffold/panes)

### Канонические раскладки

| Паттерн | Когда применять | Как адаптировать |
|---|---|---|
| Feed | Просмотр большого набора карточек или медиа | По мере роста окна меняй число и ширину колонок по минимально читаемому размеру элемента. |
| List-detail | Список и выбранный объект, например список чатов и переписка | В одной панели переходи из списка в детали. При достаточной ширине показывай их рядом и сохраняй выделение. |
| Supporting pane | Основной сценарий с контекстом, например чат и сведения о нём | Отведи основной области больше места. На узком окне раскрывай вспомогательный контент по запросу. |

[Feed](https://m3.material.io/foundations/layout/canonical-examples/feed) · [List-detail](https://m3.material.io/foundations/layout/canonical-examples/list-detail) · [Supporting pane](https://m3.material.io/foundations/layout/canonical-examples/supporting-pane)

### Десктоп, часы и XR

- **Desktop.** Учитывай изменение размеров окна, управление мышью и клавиатурой, hover, фокус и локальные действия по вторичному клику. Используй доступную ширину для связанного контента и ограничивай длину строк. [Desktop layout](https://m3.material.io/foundations/desktop/layout) · [Desktop components](https://m3.material.io/foundations/desktop/components) · [Desktop interaction](https://m3.material.io/foundations/desktop/interaction)
- **Wear OS.** Проектируй короткие сценарии для быстрого считывания. Для круглого экрана учитывай уменьшение полезной ширины к краям. Используй edge-hugging controls, arc text, отдельные стили цифр, чёрный фон и токены Wear. [Watches foundations](https://m3.material.io/foundations/watches/foundations) · [Watches styles](https://m3.material.io/foundations/watches/styles) · [Watches layout](https://m3.material.io/foundations/watches/layout)
- **XR.** Располагай окна и пространственные панели в пределах комфортной видимости и досягаемости. Учитывай разные способы ввода, читаемость на расстоянии, глубину и положение панелей. Для app bars, dialogs, nav bar, nav rail и toolbars сверяй специальные XR-спецификации. [XR design](https://m3.material.io/foundations/xr/design/overview) · [XR interaction](https://m3.material.io/foundations/xr/design/interaction) · [XR layout](https://m3.material.io/foundations/xr/design/layout) · [XR accessibility](https://m3.material.io/foundations/xr/design/accessibility) · [XR components](https://m3.material.io/foundations/xr/components/overview)

---

<a name="accessibility"></a>

## 5. Building for All — Доступность

### Потребности пользователей и co-design

Учитывай постоянные, временные и ситуационные ограничения зрения, слуха, моторики и восприятия. Вовлекай людей с разным опытом и использующих вспомогательные технологии в постановку задачи, оценку прототипа и проверку результата. Проверяй путь целиком со скринридером, клавиатурой, крупным текстом и уменьшенным движением. [User needs](https://m3.material.io/foundations/building-for-all/user-needs) · [Co-design](https://m3.material.io/foundations/building-for-all/co-design) · [Assistive technology](https://m3.material.io/foundations/overview/assistive-technology)

### Контраст и области взаимодействия

| Что проверять | Ориентир |
|---|---|
| Обычный текст | Контраст 4,5:1 или выше |
| Крупный текст (от 18 pt обычного или 14 pt полужирного) | 3:1 или выше |
| Признаки, необходимые для распознавания интерактивных элементов и их состояний | 3:1 или выше |
| Стандартная область нажатия Material | 48 × 48 dp на Android, 48 × 48 CSS px на вебе |

Измеряй контраст на фактическом фоне, включая тёмную и динамическую темы, состояние выбора и пользовательский контраст. Если визуальный элемент меньше области нажатия, проверь, что области соседних действий не перекрываются. Передавай ошибки, выбор и непрочитанное несколькими признаками: текстом, иконкой, формой или программным состоянием. [Color contrast](https://m3.material.io/foundations/designing/color-contrast) · [Elements](https://m3.material.io/foundations/designing/elements) · [Density](https://m3.material.io/foundations/layout/grids-spacing/density)

### Структура, чтение и фокус

Используй логические заголовки, доступные имена и предсказуемый порядок чтения. Tab и Shift+Tab должны последовательно обходить действия. Фокус должен быть виден. Для модальной поверхности управляй начальным фокусом, циклом обхода и возвратом к точке входа после закрытия. Дай альтернативу действиям, которые обычно выполняются жестом. При увеличении текста перераспределяй пространство и проверяй, что ключевые инструкции, ошибки и действия полностью читаются. [Structure](https://m3.material.io/foundations/designing/structure) · [Flow](https://m3.material.io/foundations/designing/flow) · [Text resizing](https://m3.material.io/foundations/writing/text-resizing) · [Text truncation](https://m3.material.io/foundations/writing/text-truncation)

---

<a name="content"></a>

## 6. Content Design — Контент

- **Тексты интерфейса.** Используй знакомые термины, короткие предложения и глаголы в подписях действий. Укажи объект там, где это проясняет последствия, например «Удалить черновик». В ошибке объясни, что случилось и как продолжить. Применяй регистр, пунктуацию и тон с учётом языка и назначения элемента. [UX writing](https://m3.material.io/foundations/content-design/style-guide/ux-writing-best-practices) · [Word choice](https://m3.material.io/foundations/content-design/style-guide/word-choice) · [Grammar & punctuation](https://m3.material.io/foundations/content-design/style-guide/grammar-and-punctuation)
- **Альтернативный текст.** Для информативного изображения коротко передай информацию, необходимую в данном контексте. Для функциональной иконки дай действию доступное имя. Декоративное изображение можно скрыть от скринридера. Сведения со сложной диаграммы вынеси в текст или таблицу. [Alt text](https://m3.material.io/foundations/content-design/alt-text)
- **Локализация.** Выбирай понятные термины без идиом, локализуй числа, даты и формы множественного числа. Оставляй место для переноса и проверяй длину строк на реальных переводах. Учитывай RTL и смешанный ввод. [Global writing](https://m3.material.io/foundations/content-design/global-writing/overview) · [Global word choice](https://m3.material.io/foundations/content-design/global-writing/word-choice)
- **Уведомления.** Начинай с события или важной информации для человека. Помести подробности в тело, добавь конкретное действие, если его можно выполнить из уведомления. Проверь обрезку длинных имён и текста в свёрнутом виде, предусмотрев короткий текст для узкого окна. Дай человеку управление категориями уведомлений. [Notifications](https://m3.material.io/foundations/content-design/notifications)

---

<a name="customization"></a>

## 7. Customization — Кастомизация

M3 спроектирован для глубокой кастомизации. Точки настройки:

1. **Color seed** — стартовый цвет, из которого генерируются палитры
2. **Color scheme variant** — стиль генерации (Tonal, Vibrant, Expressive, Content, и т.д.)
3. **Typography** — шрифты для Display, Headline, Title, Body, Label по отдельности
4. **Shape** — corner radius для каждой "семьи" компонентов
5. **Motion** — кастомные spring-параметры

Переопределяй тему через токены и проверяй результат в светлой и тёмной схеме, при пользовательском контрасте и с крупным шрифтом. Если меняешь компонент, проверь его форму, размеры, состояния и доступное имя во всех местах использования. [Customizing Material](https://m3.material.io/foundations/customization)

---

<a name="tokens"></a>

## 8. Design Tokens — Токены

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
| iOS | Токены темы, сопоставленные со стилями SwiftUI |
| Figma | Color Styles + Variables |

В Design Kit используй токены темы и слоты для сменного содержимого. Для переноса baseline и тем между инструментами Material публикует Design System Package (DSP). [Design tokens](https://m3.material.io/foundations/design-tokens/overview) · [How to use tokens](https://m3.material.io/foundations/design-tokens/how-to-use-tokens)

---

<a name="interaction"></a>

## 9. Interaction — Взаимодействие

### States — состояния

Для каждого компонента определи набор его состояний. Взаимодействие может добавлять state layer между контейнером и содержимым. Цвет слоя обычно берётся из роли контента. Слой используется по одному за раз. В общей спецификации Material значения: hover 8%, focus 10%, press 10%, drag 16%. Детали, включая фокус, ripple, disabled, selected и error, сверяй со Specs нужного компонента. Выбор и ошибка могут менять роль цвета, форму, текст или иконку. [States](https://m3.material.io/foundations/interaction/states/overview) · [State layers](https://m3.material.io/foundations/interaction/states/state-layers) · [Applying states](https://m3.material.io/foundations/interaction/states/applying-states)

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

Сохраняй системные жесты и предоставляй альтернативу для скрытых действий. [Gestures](https://m3.material.io/foundations/interaction/gestures)

### Inputs — методы ввода

M3 проектируется для **любого метода ввода**:
- Touch (палец)
- Stylus (перо)
- Mouse + Keyboard
- Game controller (для XR, Android TV)
- Voice
- Accessibility services (TalkBack, VoiceOver)

Проверь сочетание ввода на одном устройстве, включая клавиатуру и мышь, сенсорное управление и смену режима. [Inputs](https://m3.material.io/foundations/interaction/inputs)

### Selection — выбор

Паттерны выбора:
- **Single select** — radio, menu, connected button group
- **Multi select** — checkbox, filter chips, multi-select list
- **Range select** — slider, date range picker

Показывай текущий выбор визуально и передавай состояние вспомогательным технологиям. При выборе нескольких элементов обозначай количество и доступные массовые действия. [Selection](https://m3.material.io/foundations/interaction/selection)

---

<a name="layout"></a>

## 10. Layout — Раскладка

### Система отступов

База шкалы Material — **8 dp** (`md.sys.measurement.space100`). Для типовых меньших интервалов предусмотрены вложенные значения, включая 2, 4, 6 и 10 dp. Используй **padding** внутри элемента, **gap** между соседями и **margin** по внешним краям. Для серии элементов задай padding и gap на родительском контейнере. Размер области нажатия проверяй отдельно от визуальных отступов. [Spacing overview](https://m3.material.io/styles/spacing/overview) · [Applying spacing](https://m3.material.io/styles/spacing/applying-spacing) · [Spacing tokens](https://m3.material.io/styles/spacing/tokens)

### Сетки и плотность

Выбирай сетку внутри панели по доступной ширине, содержанию и минимальному размеру элемента. Меняй количество колонок, ширину промежутков и полей с учётом выбранной компоновки. Если продукт предлагает компактный режим, сделай переключатель доступным и проверь, как меняются чтение, фокус и попадание по элементам. Ориентир Material для стандартной интерактивной области — 48 × 48 dp на Android или CSS px на вебе. [Grids](https://m3.material.io/foundations/layout/grids-spacing/grids) · [Spacing](https://m3.material.io/foundations/layout/grids-spacing/spacing) · [Density](https://m3.material.io/foundations/layout/grids-spacing/density)

### RTL и системные области

Задавай края как leading/trailing (start/end), зеркаль направленные навигационные иконки и проверяй курсор, выделение, телефоны, email и URL в смешанном тексте. Направление шкал времени, медиаплееров и графиков проверяй по типу контента и локали. Учитывай вырезы, системные панели и клавиатуру. Для фолдаблов учитывай положение сгиба при разделении панелей. [Bidirectionality & RTL](https://m3.material.io/foundations/layout/bidirectionality-rtl) · [Parts of layout](https://m3.material.io/foundations/layout/layout-overview/parts-of-layout)

---

<a name="color"></a>

## 11. Color System — Цветовая система

### Роли, палитры и темы

Система M3 строит тональные палитры в HCT (Hue, Chroma, Tone). Из палитр формируется схема с семантическими ролями. Назначай роли по функции элемента и используй парные роли для содержимого. Динамическая тема меняет значения, сохраняя назначение ролей. [Color system](https://m3.material.io/styles/color/system/overview) · [How the system works](https://m3.material.io/styles/color/system/how-the-system-works)

| Роли | Назначение |
|---|---|
| `primary`, `on-primary`, `primary-container`, `on-primary-container` | Главные действия и акценты, с парой «фон и содержимое». |
| `secondary*` и `tertiary*` | Дополнительные действия, выбранные состояния и выразительные акценты. |
| `error*` | Ошибки и связанные с ними подписи или контейнеры. |
| `surface`, `surface-dim`, `surface-bright`, `surface-container-*` | Фон и уровни визуальной группировки. `on-surface` и `on-surface-variant` предназначены для содержимого. |
| `outline`, `outline-variant` | Различимые границы и менее заметные разделители. |
| `inverse-surface`, `inverse-on-surface`, `inverse-primary` | Инверсные сочетания, например для snackbar. |
| `primary-fixed*`, `secondary-fixed*`, `tertiary-fixed*` | Акцентные пары, значения которых сохраняются при переходе между светлой и тёмной темой. |
| `scrim`, `shadow` | Затемнение за модальным слоем и тени. |

Проверяй назначение точных ролей и их поддерживаемые пары. Для фиксированных цветов используй соответствующие `on-*-fixed` и `on-*-fixed-variant`. [Color roles](https://m3.material.io/styles/color/roles)

### Выбор схемы и контраст

- **Static baseline** подходит как старт для макета и резервная схема. **Static custom brand** сохраняет заданное оформление и роли бренда. Для обеих проверь светлые и тёмные значения в Theme Builder. [Choosing a scheme](https://m3.material.io/styles/color/choosing-a-scheme) · [Baseline](https://m3.material.io/styles/color/static/baseline) · [Custom brand](https://m3.material.io/styles/color/static/custom-brand)
- **Dynamic user-generated** опирается на доступную системную схему и предпочтения пользователя. **Dynamic content-based** использует выбранный цвет изображения или другого контента. Предусмотри fallback без динамического источника. Проверяй палитру на нескольких источниках и на доступных уровнях контраста: Standard, Medium и High. [Choosing a source](https://m3.material.io/styles/color/dynamic/choosing-a-source) · [User-generated source](https://m3.material.io/styles/color/dynamic/user-generated-source) · [Content-based source](https://m3.material.io/styles/color/dynamic/content-based-source)
- Для дополнений, например статусов приложения, определи семантический цвет и парные роли фона и текста, поддержи обе темы, проверь контраст и добавь смысловой признак помимо цвета. [Apply colors](https://m3.material.io/styles/color/advanced/apply-colors) · [Define new colors](https://m3.material.io/styles/color/advanced/define-new-colors) · [Adjust existing colors](https://m3.material.io/styles/color/advanced/adjust-existing-colors)

---

<a name="typography"></a>

## 12. Typography — Типографика

### Шкала типографики

M3 определяет **15 базовых стилей** в 5 категориях, каждая в 3 размерах (Large/Medium/Small):

| Категория | Назначение |
|-----------|-----------|
| Display | Самые крупные — hero, marketing |
| Headline | Заголовки разделов, страниц |
| Title | Заголовки компонентов (диалоги, карточки) |
| Label | Кнопки, chips, навигация |
| Body | Основной читаемый текст |

### Базовая шкала Roboto

На Android размер шрифта, высота строки и трекинг задаются в масштабируемых текстовых единицах. Значения ниже относятся к baseline. [Type scale & tokens](https://m3.material.io/styles/typography/type-scale-tokens)

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

### Emphasized styles

У шкалы **15 baseline и 15 emphasized-стилей**. Emphasized сохраняет место стиля в шкале и меняет вес и другие параметры. Компоненты Material используют baseline по умолчанию. Для выделенного действия, пункта меню, непрочитанного сообщения или заголовка выбери emphasized-версию соответствующего стиля, например `md.sys.typescale.emphasized.title-medium`. Проверяй переносы и ширину после смены начертания. [Type scale & tokens](https://m3.material.io/styles/typography/type-scale-tokens) · [Applying type](https://m3.material.io/styles/typography/applying-type)

### Шрифты и редакционная типографика

Roboto используется по умолчанию. Roboto Flex и другие вариативные гарнитуры можно подключать для управления весом, шириной, наклоном, optical size и grade, если шрифт и платформа поддерживают эти оси. Сверяй диапазоны конкретной гарнитуры. Проверь fallback для нужных письменностей, высоту строк и увеличение текста. Для крупных редакционных заголовков используй выразительный масштаб, контраст веса и осмысленный перенос строк. Для переписки и форм отдавай приоритет чтению и стабильной раскладке. [Fonts](https://m3.material.io/styles/typography/fonts) · [Editorial treatments](https://m3.material.io/styles/typography/editorial-treatments)

---

<a name="shape"></a>

## 13. Shape — Форма и морфинг

### Форма, углы и библиотека фигур

Форма помогает выделить действие, показать принадлежность к группе и сообщить об изменении состояния. Контраст формы особенно заметен, когда вокруг выбранного элемента сохраняется спокойная геометрия. В библиотеке Material 35 фигур для визуальных акцентов, например для обрезки изображения или аватара. Интерактивные компоненты должны сохранять узнаваемые границы, видимый фокус и область нажатия. [Overview & principles](https://m3.material.io/styles/shape/overview-principles)

| Ступень | Радиус |
|---|---:|
| None | 0 dp |
| Extra small | 4 dp |
| Small | 8 dp |
| Medium | 12 dp |
| Large | 16 dp |
| Large increased | 20 dp |
| Extra large | 28 dp |
| Extra large increased | 32 dp |
| Extra extra large | 48 dp |
| Full | Полностью округлённые углы |

Шкала применяется к симметричным и асимметричным фигурам. В connected group, split button и вертикальном меню внешние и внутренние углы помогают прочитать структуру группы. Назначай их по токенам выбранного варианта. При смене общего токена проверь все использующие его компоненты. [Corner radius scale](https://m3.material.io/styles/shape/corner-radius-scale)

### Shape morph

Морфинг связывает два состояния, например при нажатии кнопки, выборе варианта или ожидании ответа. Сохраняй пространственную связь между исходным и конечным состояниями. Передавай значение и без анимации, например через выбранную иконку или метку. Для формы используй spatial motion. [Shape morph](https://m3.material.io/styles/shape/shape-morph)

---

<a name="motion"></a>

## 14. Motion — Движение (Spring Physics)

### Схемы и токены движения

У Material две схемы движения: **Expressive** с более заметным отскоком и **Standard** с более сдержанной реакцией. Выбери общую схему продукта и меняй её в отдельных сценариях, если степень акцента оправдана. Поведение пружины задают жёсткость, демпфирование и начальная скорость. При перенацеливании пружины меняется конечное значение, а движение продолжается с текущей скоростью.

| Семейство | Свойства | Когда использовать |
|---|---|---|
| Spatial | Позиция, поворот, размер и форма | При перемещении и морфинге. Возможен overshoot в зависимости от схемы. |
| Effects | Цвет и прозрачность | Для плавной смены визуального состояния без overshoot. |

Обе группы имеют скорости `fast`, `default`, `slow`. Начни с `default`. Для небольших объектов вроде кнопки обычно подходит `fast`, для полноэкранных изменений — `slow`. Пример семантического токена — `md.sys.motion.spring.fast.spatial`. Схема выбирается отдельно, фактические параметры бери из её спецификации. [Как работают пружины](https://m3.material.io/styles/motion/overview/how-it-works) · [Спецификации](https://m3.material.io/styles/motion/overview/specs) · [Motion theming](https://m3.material.io/blog/m3-expressive-motion-theming)

### Паттерны переходов

- **Container transform** сохраняет связь между источником и раскрытым содержимым, например между карточкой и деталями.
- **Shared axis** использует общую ось для связанных экранов или последовательных шагов.
- **Fade through** последовательно меняет видимость поверхностей для перехода между слабо связанными состояниями.
- **Fade** подходит для появления или исчезновения элемента внутри текущего контекста.

Проверяй обратный переход, прерывание жеста, повторный ввод и восстановление фокуса. Для анимаций без жестов и прерываний на вебе можно использовать кривые приближения из Specs. Для существующих реализаций с easing и duration сверяй соответствующие legacy-токены. [Transition patterns](https://m3.material.io/styles/motion/transitions/transition-patterns) · [Applying transitions](https://m3.material.io/styles/motion/transitions/applying-transitions) · [Easing & duration](https://m3.material.io/styles/motion/easing-and-duration/tokens-specs)

### Уменьшенное движение

Учитывай настройки платформы и `prefers-reduced-motion` в вебе. Сократи или пропусти пространственное движение и повторяющиеся эффекты, где это позволяет сценарий. Состояние, результат, выбор и связь с исходным элементом должны считываться по тексту, форме, фокусу или другим визуальным признакам.

---

<a name="elevation"></a>

## 15. Elevation — Высота

Elevation описывает расстояние между поверхностями по оси Z. Разница тонов, тень и scrim помогают показать границы и перекрытие. Поверхности `surface` и `surface-container-*` выбираются по роли в компоновке, их тона не привязаны к уровню elevation. Для модальной поверхности уместен scrim. Для плавающего элемента проверь границу на реальном фоне в обеих темах.

| Уровень | Высота |
|---|---:|
| 0 | 0 dp |
| +1 | 1 dp |
| +2 | 3 dp |
| +3 | 6 dp |
| +4 | 8 dp |
| +5 | 12 dp |

В покое Material использует уровни 0…+3. Уровни +4 и +5 предназначены для состояний взаимодействия, если их задаёт конкретный компонент. [Elevation overview](https://m3.material.io/styles/elevation/overview) · [Applying elevation](https://m3.material.io/styles/elevation/applying-elevation) · [Elevation tokens](https://m3.material.io/styles/elevation/tokens)

---

<a name="icons"></a>

## 16. Iconography — Иконки

### Material Symbols

Доступны Outlined, Rounded и Sharp. Выбери один основной стиль для набора, используй `FILL` для осмысленного выделения и учитывай возможности гарнитуры. Остальные оси — `wght` (толщина штриха), `GRAD` (тонкая настройка визуального веса), `opsz` (оптический размер). Для обычной иконки отправной размер — 24 dp. Material также выделяет оптические размеры 20 dp для плотных интерфейсов и 40/48 dp для крупных элементов. Размер иконки в конкретном компоненте сверяй с его Specs.

Сохраняй простой узнаваемый силуэт. Подписывай незнакомые символы видимым текстом и давай отдельное доступное имя кнопке с иконкой. Направленные символы, например Back, проверяй в RTL. [Icons](https://m3.material.io/styles/icons/overview) · [Designing icons](https://m3.material.io/styles/icons/designing-icons) · [Applying icons](https://m3.material.io/styles/icons/applying-icons)

---

<a name="components"></a>

## 17. Components — Компоненты

В **Overview** найди назначение, варианты, историю обновлений и доступность по платформам. В **Specs** выбери нужную конфигурацию и проверь токены, геометрию и состояния. **Guidelines** описывает размещение и поведение, **Accessibility** — семантику, фокус и ввод. Если страница содержит baseline и Expressive, используй размеры и токены выбранного варианта. [Каталог компонентов](https://m3.material.io/components)

### Действия

| Компонент | Как выбирать и применять |
|---|---|
| [Buttons](https://m3.material.io/components/buttons/specs) | Default запускает действие, toggle хранит состояние. Цветовые стили: elevated, filled, tonal, outlined и text. Toggle использует первые четыре. Expressive добавляет round/square и размеры XS–XL, среди которых S — стандартный. Визуальная высота и область нажатия измеряются отдельно. |
| [Icon buttons](https://m3.material.io/components/icon-buttons/guidelines) | Компактное действие с узнаваемой иконкой. Дай доступное имя, для toggle передавай состояние. Проверь варианты ширины, формы и приоритета по Specs. |
| [Button groups](https://m3.material.io/components/button-groups/guidelines) | Группируй связанные кнопки и icon buttons. Standard оставляет визуальный зазор. Connected сближает элементы и заменяет baseline segmented buttons в новых Expressive-макетах. Определи одиночный или множественный выбор и обязательность выбора. |
| [Split button](https://m3.material.io/components/split-button/guidelines) | Ведущая часть выполняет действие по умолчанию, замыкающая раскрывает связанные варианты. Пример — «Сохранить» и меню вариантов сохранения. У обеих частей должны быть различимые цели и отдельные доступные имена. |
| [FAB](https://m3.material.io/components/floating-action-button/guidelines), [Extended FAB](https://m3.material.io/components/extended-fab/guidelines), [FAB menu](https://m3.material.io/components/fab-menu/guidelines) | FAB выделяет главное действие экрана. Extended FAB добавляет подпись. FAB menu раскрывает компактный набор связанных действий создания, например новый личный чат или новая группа. |
| [Toolbars](https://m3.material.io/components/toolbars/guidelines) | Docked toolbar занимает край экрана и заменяет bottom app bar. Floating toolbar поддерживает горизонтальное и вертикальное размещение и сочетание с FAB. Для обеих есть Standard и Vibrant. Material рекомендует избегать одновременного показа toolbar и navigation bar. |

[All buttons](https://m3.material.io/components/all-buttons) · [Segmented buttons](https://m3.material.io/components/segmented-buttons/overview)

### Навигация, поиск и организация контента

| Компонент | Как выбирать и применять |
|---|---|
| [Navigation bar](https://m3.material.io/components/navigation-bar/guidelines) | Для 3–5 равноправных основных разделов. Flexible navigation bar поддерживает горизонтальную раскладку пунктов на medium. Сохраняй порядок и названия разделов при смене компоновки. |
| [Navigation rail](https://m3.material.io/components/navigation-rail/guidelines), [Navigation drawer](https://m3.material.io/components/navigation-drawer/overview) | Новые варианты rail — collapsed и expanded. Expanded rail заменяет drawer в Expressive и может быть стандартным или модальным. Для жеста закрытия обеспечь также явное управление и возврат фокуса. |
| [App bars](https://m3.material.io/components/app-bars/guidelines) | Варианты Search app bar, Small, Medium flexible, Large flexible. Для новых экранов выбирай flexible-варианты вместо baseline Medium и Large. Оставляй одно главное действие, при необходимости два. Поиск как ключевую точку входа можно разместить в Search app bar. |
| [Search](https://m3.material.io/components/search/guidelines) | Рекомендованный contained-вариант сохраняет залитый контейнер. Divided визуально разделяет элементы. При фокусе поле расширяется, результаты и подсказки можно группировать отступами. Проверь ведущие и замыкающие действия, очистку запроса, переход к результатам и возврат фокуса. |
| [Tabs](https://m3.material.io/components/tabs/guidelines) | Для соседних групп контента одного уровня. Primary обозначают основные группы текущей области, secondary — подразделы. Для длинных наборов предусмотрена горизонтальная прокрутка. |
| [Lists](https://m3.material.io/components/lists/guidelines) | Используй стандартное или сегментированное Expressive-оформление. Leading, текстовый и trailing-слоты позволяют разместить аватар, заголовок, превью и действие. Проверяй форму элементов на краях группы, выделение выбора и чтение при увеличении шрифта. |
| [Cards](https://m3.material.io/components/cards/guidelines), [Divider](https://m3.material.io/components/divider/guidelines) | Карточка объединяет контент об одном объекте и его действия. Есть elevated, filled и outlined. Divider разделяет связанные группы, когда одной разницы в отступах недостаточно. |
| [Carousel](https://m3.material.io/components/carousel/guidelines) | Подходит для медиа, которые имеет смысл листать. Варианты включают multi-browse, uncontained, uncontained multi-aspect ratio, hero, center-aligned hero и full-screen. Доступные элементы и порядок должны сохраняться при клавиатурной навигации и уменьшенном движении. |

### Ввод и выбор

| Компонент | Как выбирать и применять |
|---|---|
| [Text fields](https://m3.material.io/components/text-fields/guidelines) | Filled или outlined. Подпись должна сохранять смысл после ввода. Используй вспомогательный текст для формата, сообщение об ошибке для её причины и способа исправления. |
| [Checkbox](https://m3.material.io/components/checkbox/guidelines), [Radio button](https://m3.material.io/components/radio-button/guidelines), [Switch](https://m3.material.io/components/switch/guidelines) | Checkbox — независимые варианты или множественный выбор, radio — один вариант, switch — немедленное включение или выключение настройки. Для группы «Выбрать всё» учитывай частичное состояние. |
| [Chips](https://m3.material.io/components/chips/guidelines) | Assist запускает действие, filter сужает выдачу, input представляет уже введённый объект, suggestion предлагает следующий ввод или действие. Для удаляемого input chip обеспечь отдельную операцию удаления. |
| [Sliders](https://m3.material.io/components/sliders/guidelines) | Standard, centered или range. Настрой диапазон, шаг, горизонтальную или вертикальную ориентацию, размер и, для standard, опциональную иконку. При важной точности предложи числовой ввод. |
| [Scroll fields](https://m3.material.io/components/scroll-fields/guidelines) | Для небольшого набора упорядоченных значений, например измерений. Подписывай каждое поле, показывай текущее значение и обеспечь переход к текстовому вводу. |
| [Date pickers](https://m3.material.io/components/date-pickers/guidelines), [Time pickers](https://m3.material.io/components/time-pickers/guidelines) | Для даты подойдут выбор в календаре, диапазон или ввод в зависимости от задачи. Для часов и минут предусмотрены dial в модальном окне, inline scroll и текстовый ввод. Expressive-оформление времени, обновлённое в октябре 2026, называется `vibrant` в Compose. Проверяй формат 12/24 часа по локали. |

### Всплывающие поверхности и обратная связь

| Компонент | Как выбирать и применять |
|---|---|
| [Menus](https://m3.material.io/components/menus/guidelines) | Временный набор действий или вариантов. Expressive vertical menus поддерживают Standard и Vibrant, интервалы, разделители, выделение выбора и анимацию подменю. Для контекстного меню добавь доступный способ открыть его, помимо долгого нажатия или вторичного клика. |
| [Dialogs](https://m3.material.io/components/dialogs/guidelines) | Для решения, требующего внимания до продолжения. Сформулируй цель в заголовке, а последствия и выбор — в тексте и подписях действий. Проверь начальный фокус, Back/Escape и возврат фокуса. |
| [Bottom sheets](https://m3.material.io/components/bottom-sheets/guidelines), [Side sheets](https://m3.material.io/components/side-sheets/guidelines) | Представляют дополнительные действия или контент. Выбери положение и модальность по ширине окна и связи с основным экраном. Сделай закрытие доступным помимо жеста. |
| [Snackbar](https://m3.material.io/components/snackbar/guidelines) | Краткая обратная связь с необязательным действием, например «Отменить». Длительные ошибки и блокирующие условия требуют постоянного сообщения или отдельного решения. |
| [Tooltips](https://m3.material.io/components/tooltips/guidelines) | Plain tooltip поясняет элемент, rich tooltip добавляет контекст. Учитывай фокус и hover, обеспечь доступность важной информации без наведения. |
| [Badges](https://m3.material.io/components/badges/guidelines) | Индикатор активности или количество. Объявляй смысл счётчика в доступном описании связанного элемента, например «Чаты, 3 непрочитанных». |

### Ожидание и прогресс

| Ожидаемое время | Компонент |
|---|---|
| Меньше 200 мс | Показать результат без индикатора |
| 200 мс–5 с, длительность неизвестна | Loading indicator с морфингом. Вариант с контейнером подходит поверх контента и для pull-to-refresh. |
| Дольше 5 с | Linear или circular progress indicator. Начни с indeterminate, если доля работы неизвестна. Перейди к determinate, когда есть достоверное значение. |

Выбирай индикатор по ожидаемому сценарию в начале операции. Переход от неизвестной доли к известной выполняй внутри семейства progress indicators. Для длительной операции учитывай отмену или продолжение в фоне, если сценарий это допускает. Объявляй статус и существенные этапы для скринридера. [Loading indicator](https://m3.material.io/components/loading-indicator/guidelines) · [Progress indicators](https://m3.material.io/components/progress-indicators/guidelines) · [Progress accessibility](https://m3.material.io/components/progress-indicators/accessibility)

---

<a name="develop"></a>

## 18. Develop — Реализация на платформах

| Платформа | Поддержка и способ внедрения |
|---|---|
| Android / Jetpack Compose | Material Android перешёл на Compose-first. **Compose Material 3 1.5.0** вышел 18 ноября 2026. Стабилизированы, в частности, split buttons, button groups, морфинг toggle buttons и chips, floating toolbars, wavy progress, вложенные меню, сегментированные строки и Expressive-темизация. Статус opt-in проверяй для конкретного API в версии проекта. Material 2 для Compose выведен из активной поддержки. [Develop: Compose](https://m3.material.io/develop/android/jetpack-compose) · [Анонс 1.5.0](https://m3.material.io/blog/compose-material-3-is-stable) |
| Android / Views | MDC-Android 1.14 — последний стабильный выпуск Material Views. Библиотека в режиме поддержки с критическими исправлениями. Для существующих экранов выбирай доступные в установленной версии компоненты, для миграции используй поэкранный переход к Compose. [Develop: Android Views](https://m3.material.io/develop/android/mdc-android) · [Compose-first](https://m3.material.io/blog/material-is-compose-first) |
| Flutter | Официальный Flutter-каталог поддерживает базовые компоненты Material 3. Официальный набор Expressive на странице Flutter обозначен как недоступный. Для Expressive-экрана переноси выбранные токены и проверяй доступность каждого элемента в своём SDK. [Develop: Flutter](https://m3.material.io/develop/flutter) |
| Web | Material Web находится в режиме поддержки. Google указывает, что Expressive в Material Web не реализован. В веб-клиенте можно применить принципы, токены и подходящие паттерны компонентов с помощью CSS и доступных HTML-примитивов. [Develop: Web](https://m3.material.io/develop/web) · [Motion specs](https://m3.material.io/styles/motion/overview/specs) |
| Другие клиенты | Используй общий смысл ролей, состояний и сценариев, затем сопоставь их с нативными контролами, поведением клавиатуры, скринридером и системной темой каждой платформы. |

Начни с одного сценария. Выбери акценты, тему и раскладки, собери прототип в Material 3 Design Kit, сверяй варианты и токены с каталогом. В реализации сохрани семантику, проверяй состояния, ошибки, обе темы, поддержку контраста, увеличение текста и смену размера окна. [Get started](https://m3.material.io/get-started) · [Develop](https://m3.material.io/develop) · [Material 3 Design Kit](https://www.figma.com/community/file/1035203688168086460)

---

<a name="barkfluff"></a>

## 19. Применение в BarkFluff

| Сценарий | Решение в макете |
|---|---|
| Список чатов и переписка | List-detail с одной панелью на узком окне и двумя при достаточной ширине. При изменении окна сохраняй выбранный чат и черновик. Непрочитанные сообщения обозначай сочетанием начертания и текстового либо доступного статуса. |
| Сведения о чате | Supporting pane на широком окне. На узком окне используй открываемую поверхность и после закрытия возвращай фокус к вызвавшему элементу. |
| Поиск сообщений | Напиши, где идёт поиск, и покажи выбранные фильтры, пустую выдачу, ожидание и ошибку отдельными состояниями. Сохраняй запрос при просмотре результата и возврате. |
| Отправка вложения | Показывай фактическую долю отправки, когда она известна. Предусмотри читаемую ошибку и существующие в клиенте способы отмены и повтора. |
| Звонок | Визуально отличай вызов, принятие и завершение, подписывай переключатели микрофона и камеры, обозначай их состояние. Удерживай действия доступными при повороте и изменении размера окна. |

Проверяй сценарии по [продуктовому дизайн-документу](dd.md) и текущей реализации клиента. Android-проект закрепляет зависимости в [каталоге версий](../Android/gradle/libs.versions.toml). Версия библиотеки в официальных примерах может отличаться от проектной, поэтому сверяй конкретный API и его статус перед использованием.

---

<a name="source-map"></a>

## 20. Карта официальных страниц

### Home, Get started, Develop и Blog

[Home](https://m3.material.io/) · [Get started](https://m3.material.io/get-started) · [Develop overview](https://m3.material.io/develop) · [Android Views](https://m3.material.io/develop/android/mdc-android) · [Jetpack Compose](https://m3.material.io/develop/android/jetpack-compose) · [Flutter](https://m3.material.io/develop/flutter) · [Web](https://m3.material.io/develop/web) · [Blog](https://m3.material.io/blog) · [Search](https://m3.material.io/search.html)

- **Релизы.** Сверяй даты и статус поддержки по первичным анонсам. [Введение в Expressive](https://m3.material.io/blog/building-with-m3-expressive) · [I/O 2026](https://m3.material.io/blog/whats-new-at-io26) · [Compose-first](https://m3.material.io/blog/material-is-compose-first) · [Стабильный Compose Material 3](https://m3.material.io/blog/compose-material-3-is-stable)
- **Прототипы.** Используй слоты Figma для вариантов компонентов со связью с библиотекой. Когда сравниваешь несколько размеров, форм или отступов, проверяй комбинации в интерактивном прототипе. [Slots in Figma](https://m3.material.io/blog/material-3-slot-components-figma) · [Parametric prototyping](https://m3.material.io/blog/enhance-your-component-designs-with-parametric-prototyping)
- **Исследования и прикладные разборы.** Используй результаты тестов как источник гипотез для продукта. Отдельно проверяй распознаваемость действия, понимание состояния и успешность выполнения задачи. [Testing Material 3](https://m3.material.io/blog/testing-material-3) · [Carousel research](https://m3.material.io/blog/material-3-carousel-research-design) · [24-hour clock research](https://m3.material.io/blog/24-hour-clock-design-research) · [Data visualization accessibility](https://m3.material.io/blog/data-visualization-accessibility) · [Motion theming](https://m3.material.io/blog/m3-expressive-motion-theming)

### Foundations

[Foundations overview](https://m3.material.io/foundations)

| Тема | Страницы и вкладки |
|---|---|
| Доступность | [Co design](https://m3.material.io/foundations/building-for-all/co-design) · [User needs](https://m3.material.io/foundations/building-for-all/user-needs) · [Color contrast](https://m3.material.io/foundations/designing/color-contrast) · [Elements](https://m3.material.io/foundations/designing/elements) · [Flow](https://m3.material.io/foundations/designing/flow) · [Designing overview](https://m3.material.io/foundations/designing/overview) · [Structure](https://m3.material.io/foundations/designing/structure) · [Assistive technology](https://m3.material.io/foundations/overview/assistive-technology) · [Principles](https://m3.material.io/foundations/overview/principles) · [Best practices](https://m3.material.io/foundations/writing/best-practices) · [Text resizing](https://m3.material.io/foundations/writing/text-resizing) · [Text truncation](https://m3.material.io/foundations/writing/text-truncation) |
| Контент | [Alt text](https://m3.material.io/foundations/content-design/alt-text) · [Global writing overview](https://m3.material.io/foundations/content-design/global-writing/overview) · [Global word choice](https://m3.material.io/foundations/content-design/global-writing/word-choice) · [Notifications](https://m3.material.io/foundations/content-design/notifications) · [Content design overview](https://m3.material.io/foundations/content-design/overview) · [Grammar and punctuation](https://m3.material.io/foundations/content-design/style-guide/grammar-and-punctuation) · [Ux writing best practices](https://m3.material.io/foundations/content-design/style-guide/ux-writing-best-practices) · [Word choice](https://m3.material.io/foundations/content-design/style-guide/word-choice) |
| Кастомизация и токены | [Customization](https://m3.material.io/foundations/customization) · [How to use tokens](https://m3.material.io/foundations/design-tokens/how-to-use-tokens) · [Overview](https://m3.material.io/foundations/design-tokens/overview) |
| Взаимодействие | [Gestures](https://m3.material.io/foundations/interaction/gestures) · [Inputs](https://m3.material.io/foundations/interaction/inputs) · [Selection](https://m3.material.io/foundations/interaction/selection) · [Applying states](https://m3.material.io/foundations/interaction/states/applying-states) · [Overview](https://m3.material.io/foundations/interaction/states/overview) · [State layers](https://m3.material.io/foundations/interaction/states/state-layers) |
| Устройство раскладки | [Adaptive design](https://m3.material.io/foundations/layout/layout-overview/adaptive-design) · [Layout overview](https://m3.material.io/foundations/layout/layout-overview/overview) · [Parts of layout](https://m3.material.io/foundations/layout/layout-overview/parts-of-layout) · [Bars](https://m3.material.io/foundations/layout/scaffold/bars) · [Scaffold overview](https://m3.material.io/foundations/layout/scaffold/overview) · [Panes](https://m3.material.io/foundations/layout/scaffold/panes) · [Rails](https://m3.material.io/foundations/layout/scaffold/rails) |
| Сетки, плотность, RTL | [Bidirectionality & RTL](https://m3.material.io/foundations/layout/bidirectionality-rtl) · [Density](https://m3.material.io/foundations/layout/grids-spacing/density) · [Grids](https://m3.material.io/foundations/layout/grids-spacing/grids) · [Grids & spacing overview](https://m3.material.io/foundations/layout/grids-spacing/overview) · [Spacing](https://m3.material.io/foundations/layout/grids-spacing/spacing) |
| Breakpoints | [Compact](https://m3.material.io/foundations/layout/breakpoints/compact) · [Expanded](https://m3.material.io/foundations/layout/breakpoints/expanded) · [Large extra large](https://m3.material.io/foundations/layout/breakpoints/large-extra-large) · [Medium](https://m3.material.io/foundations/layout/breakpoints/medium) · [Overview](https://m3.material.io/foundations/layout/breakpoints/overview) |
| Канонические примеры | [Feed](https://m3.material.io/foundations/layout/canonical-examples/feed) · [List detail](https://m3.material.io/foundations/layout/canonical-examples/list-detail) · [Overview](https://m3.material.io/foundations/layout/canonical-examples/overview) · [Supporting pane](https://m3.material.io/foundations/layout/canonical-examples/supporting-pane) |
| Desktop | [Components](https://m3.material.io/foundations/desktop/components) · [Interaction](https://m3.material.io/foundations/desktop/interaction) · [Layout](https://m3.material.io/foundations/desktop/layout) · [Overview](https://m3.material.io/foundations/desktop/overview) |
| Часы | [Foundations](https://m3.material.io/foundations/watches/foundations) · [Layout](https://m3.material.io/foundations/watches/layout) · [Overview](https://m3.material.io/foundations/watches/overview) · [Styles](https://m3.material.io/foundations/watches/styles) |
| XR design | [Accessibility](https://m3.material.io/foundations/xr/design/accessibility) · [Interaction](https://m3.material.io/foundations/xr/design/interaction) · [Layout](https://m3.material.io/foundations/xr/design/layout) · [Overview](https://m3.material.io/foundations/xr/design/overview) |
| XR components | [App bars](https://m3.material.io/foundations/xr/components/app-bars) · [Dialogs](https://m3.material.io/foundations/xr/components/dialogs) · [Nav bar](https://m3.material.io/foundations/xr/components/nav-bar) · [Nav rail](https://m3.material.io/foundations/xr/components/nav-rail) · [Overview](https://m3.material.io/foundations/xr/components/overview) · [Toolbars](https://m3.material.io/foundations/xr/components/toolbars) |
| Usability и словарь | [Glossary](https://m3.material.io/foundations/glossary) · [Applying m 3 expressive](https://m3.material.io/foundations/usability/applying-m-3-expressive) · [Usability overview](https://m3.material.io/foundations/usability/overview) |

### Styles

[Styles overview](https://m3.material.io/styles)

| Тема | Страницы и вкладки |
|---|---|
| Цветовая система и роли | [Choosing a scheme](https://m3.material.io/styles/color/choosing-a-scheme) · [Resources](https://m3.material.io/styles/color/resources) · [Color roles](https://m3.material.io/styles/color/roles) · [Custom colors](https://m3.material.io/styles/color/roles/custom-colors) · [How the system works](https://m3.material.io/styles/color/system/how-the-system-works) · [Color system overview](https://m3.material.io/styles/color/system/overview) |
| Статические и динамические схемы | [Choosing a source](https://m3.material.io/styles/color/dynamic/choosing-a-source) · [Content based source](https://m3.material.io/styles/color/dynamic/content-based-source) · [User generated source](https://m3.material.io/styles/color/dynamic/user-generated-source) · [Baseline](https://m3.material.io/styles/color/static/baseline) · [Custom brand](https://m3.material.io/styles/color/static/custom-brand) |
| Настройка цвета | [Adjust existing colors](https://m3.material.io/styles/color/advanced/adjust-existing-colors) · [Apply colors](https://m3.material.io/styles/color/advanced/apply-colors) · [Define new colors](https://m3.material.io/styles/color/advanced/define-new-colors) · [Overview](https://m3.material.io/styles/color/advanced/overview) |
| Elevation | [Applying elevation](https://m3.material.io/styles/elevation/applying-elevation) · [Overview](https://m3.material.io/styles/elevation/overview) · [Tokens](https://m3.material.io/styles/elevation/tokens) |
| Иконки | [Applying icons](https://m3.material.io/styles/icons/applying-icons) · [Designing icons](https://m3.material.io/styles/icons/designing-icons) · [Overview](https://m3.material.io/styles/icons/overview) |
| Пружины | [How it works](https://m3.material.io/styles/motion/overview/how-it-works) · [Specs](https://m3.material.io/styles/motion/overview/specs) |
| Easing, duration и переходы | [Applying easing and duration](https://m3.material.io/styles/motion/easing-and-duration/applying-easing-and-duration) · [Tokens specs](https://m3.material.io/styles/motion/easing-and-duration/tokens-specs) · [Applying transitions](https://m3.material.io/styles/motion/transitions/applying-transitions) · [Transition patterns](https://m3.material.io/styles/motion/transitions/transition-patterns) |
| Форма | [Corner radius scale](https://m3.material.io/styles/shape/corner-radius-scale) · [Overview principles](https://m3.material.io/styles/shape/overview-principles) · [Shape morph](https://m3.material.io/styles/shape/shape-morph) |
| Отступы | [Applying spacing](https://m3.material.io/styles/spacing/applying-spacing) · [Overview](https://m3.material.io/styles/spacing/overview) · [Tokens](https://m3.material.io/styles/spacing/tokens) |
| Типографика | [Applying type](https://m3.material.io/styles/typography/applying-type) · [Editorial treatments](https://m3.material.io/styles/typography/editorial-treatments) · [Fonts](https://m3.material.io/styles/typography/fonts) · [Overview](https://m3.material.io/styles/typography/overview) · [Type scale tokens](https://m3.material.io/styles/typography/type-scale-tokens) |

### Components

[Components overview](https://m3.material.io/components)

| Тема | Страницы и вкладки |
|---|---|
| Указатель кнопок | [All buttons](https://m3.material.io/components/all-buttons) |
| App bars | [Overview](https://m3.material.io/components/app-bars/overview) · [Specs](https://m3.material.io/components/app-bars/specs) · [Guidelines](https://m3.material.io/components/app-bars/guidelines) · [Accessibility](https://m3.material.io/components/app-bars/accessibility) |
| Badges | [Overview](https://m3.material.io/components/badges/overview) · [Specs](https://m3.material.io/components/badges/specs) · [Guidelines](https://m3.material.io/components/badges/guidelines) · [Accessibility](https://m3.material.io/components/badges/accessibility) |
| Button groups | [Overview](https://m3.material.io/components/button-groups/overview) · [Specs](https://m3.material.io/components/button-groups/specs) · [Guidelines](https://m3.material.io/components/button-groups/guidelines) · [Accessibility](https://m3.material.io/components/button-groups/accessibility) |
| Buttons | [Overview](https://m3.material.io/components/buttons/overview) · [Specs](https://m3.material.io/components/buttons/specs) · [Guidelines](https://m3.material.io/components/buttons/guidelines) · [Accessibility](https://m3.material.io/components/buttons/accessibility) |
| Extended FABs | [Overview](https://m3.material.io/components/extended-fab/overview) · [Specs](https://m3.material.io/components/extended-fab/specs) · [Guidelines](https://m3.material.io/components/extended-fab/guidelines) · [Accessibility](https://m3.material.io/components/extended-fab/accessibility) |
| FAB menu | [Overview](https://m3.material.io/components/fab-menu/overview) · [Specs](https://m3.material.io/components/fab-menu/specs) · [Guidelines](https://m3.material.io/components/fab-menu/guidelines) · [Accessibility](https://m3.material.io/components/fab-menu/accessibility) |
| FABs | [Overview](https://m3.material.io/components/floating-action-button/overview) · [Specs](https://m3.material.io/components/floating-action-button/specs) · [Guidelines](https://m3.material.io/components/floating-action-button/guidelines) · [Accessibility](https://m3.material.io/components/floating-action-button/accessibility) |
| Icon buttons | [Overview](https://m3.material.io/components/icon-buttons/overview) · [Specs](https://m3.material.io/components/icon-buttons/specs) · [Guidelines](https://m3.material.io/components/icon-buttons/guidelines) · [Accessibility](https://m3.material.io/components/icon-buttons/accessibility) |
| Segmented buttons | [Overview](https://m3.material.io/components/segmented-buttons/overview) · [Specs](https://m3.material.io/components/segmented-buttons/specs) · [Guidelines](https://m3.material.io/components/segmented-buttons/guidelines) · [Accessibility](https://m3.material.io/components/segmented-buttons/accessibility) |
| Split buttons | [Overview](https://m3.material.io/components/split-button/overview) · [Specs](https://m3.material.io/components/split-button/specs) · [Guidelines](https://m3.material.io/components/split-button/guidelines) · [Accessibility](https://m3.material.io/components/split-button/accessibility) |
| Cards | [Overview](https://m3.material.io/components/cards/overview) · [Specs](https://m3.material.io/components/cards/specs) · [Guidelines](https://m3.material.io/components/cards/guidelines) · [Accessibility](https://m3.material.io/components/cards/accessibility) |
| Carousel | [Overview](https://m3.material.io/components/carousel/overview) · [Specs](https://m3.material.io/components/carousel/specs) · [Guidelines](https://m3.material.io/components/carousel/guidelines) · [Accessibility](https://m3.material.io/components/carousel/accessibility) |
| Checkbox | [Overview](https://m3.material.io/components/checkbox/overview) · [Specs](https://m3.material.io/components/checkbox/specs) · [Guidelines](https://m3.material.io/components/checkbox/guidelines) · [Accessibility](https://m3.material.io/components/checkbox/accessibility) |
| Chips | [Overview](https://m3.material.io/components/chips/overview) · [Specs](https://m3.material.io/components/chips/specs) · [Guidelines](https://m3.material.io/components/chips/guidelines) · [Accessibility](https://m3.material.io/components/chips/accessibility) |
| Date pickers | [Overview](https://m3.material.io/components/date-pickers/overview) · [Specs](https://m3.material.io/components/date-pickers/specs) · [Guidelines](https://m3.material.io/components/date-pickers/guidelines) · [Accessibility](https://m3.material.io/components/date-pickers/accessibility) |
| Time pickers | [Overview](https://m3.material.io/components/time-pickers/overview) · [Specs](https://m3.material.io/components/time-pickers/specs) · [Guidelines](https://m3.material.io/components/time-pickers/guidelines) · [Accessibility](https://m3.material.io/components/time-pickers/accessibility) |
| Dialogs | [Overview](https://m3.material.io/components/dialogs/overview) · [Specs](https://m3.material.io/components/dialogs/specs) · [Guidelines](https://m3.material.io/components/dialogs/guidelines) · [Accessibility](https://m3.material.io/components/dialogs/accessibility) |
| Divider | [Overview](https://m3.material.io/components/divider/overview) · [Specs](https://m3.material.io/components/divider/specs) · [Guidelines](https://m3.material.io/components/divider/guidelines) · [Accessibility](https://m3.material.io/components/divider/accessibility) |
| Lists | [Overview](https://m3.material.io/components/lists/overview) · [Specs](https://m3.material.io/components/lists/specs) · [Guidelines](https://m3.material.io/components/lists/guidelines) · [Accessibility](https://m3.material.io/components/lists/accessibility) |
| Loading indicator | [Overview](https://m3.material.io/components/loading-indicator/overview) · [Specs](https://m3.material.io/components/loading-indicator/specs) · [Guidelines](https://m3.material.io/components/loading-indicator/guidelines) · [Accessibility](https://m3.material.io/components/loading-indicator/accessibility) |
| Progress indicators | [Overview](https://m3.material.io/components/progress-indicators/overview) · [Specs](https://m3.material.io/components/progress-indicators/specs) · [Guidelines](https://m3.material.io/components/progress-indicators/guidelines) · [Accessibility](https://m3.material.io/components/progress-indicators/accessibility) |
| Menus | [Overview](https://m3.material.io/components/menus/overview) · [Specs](https://m3.material.io/components/menus/specs) · [Guidelines](https://m3.material.io/components/menus/guidelines) · [Accessibility](https://m3.material.io/components/menus/accessibility) |
| Navigation bar | [Overview](https://m3.material.io/components/navigation-bar/overview) · [Specs](https://m3.material.io/components/navigation-bar/specs) · [Guidelines](https://m3.material.io/components/navigation-bar/guidelines) · [Accessibility](https://m3.material.io/components/navigation-bar/accessibility) |
| Navigation drawer | [Overview](https://m3.material.io/components/navigation-drawer/overview) · [Specs](https://m3.material.io/components/navigation-drawer/specs) · [Guidelines](https://m3.material.io/components/navigation-drawer/guidelines) · [Accessibility](https://m3.material.io/components/navigation-drawer/accessibility) |
| Navigation rail | [Overview](https://m3.material.io/components/navigation-rail/overview) · [Specs](https://m3.material.io/components/navigation-rail/specs) · [Guidelines](https://m3.material.io/components/navigation-rail/guidelines) · [Accessibility](https://m3.material.io/components/navigation-rail/accessibility) |
| Radio button | [Overview](https://m3.material.io/components/radio-button/overview) · [Specs](https://m3.material.io/components/radio-button/specs) · [Guidelines](https://m3.material.io/components/radio-button/guidelines) · [Accessibility](https://m3.material.io/components/radio-button/accessibility) |
| Scroll fields | [Overview](https://m3.material.io/components/scroll-fields/overview) · [Specs](https://m3.material.io/components/scroll-fields/specs) · [Guidelines](https://m3.material.io/components/scroll-fields/guidelines) · [Accessibility](https://m3.material.io/components/scroll-fields/accessibility) |
| Search | [Overview](https://m3.material.io/components/search/overview) · [Specs](https://m3.material.io/components/search/specs) · [Guidelines](https://m3.material.io/components/search/guidelines) · [Accessibility](https://m3.material.io/components/search/accessibility) |
| Bottom sheets | [Overview](https://m3.material.io/components/bottom-sheets/overview) · [Specs](https://m3.material.io/components/bottom-sheets/specs) · [Guidelines](https://m3.material.io/components/bottom-sheets/guidelines) · [Accessibility](https://m3.material.io/components/bottom-sheets/accessibility) |
| Side sheets | [Overview](https://m3.material.io/components/side-sheets/overview) · [Specs](https://m3.material.io/components/side-sheets/specs) · [Guidelines](https://m3.material.io/components/side-sheets/guidelines) · [Accessibility](https://m3.material.io/components/side-sheets/accessibility) |
| Sliders | [Overview](https://m3.material.io/components/sliders/overview) · [Specs](https://m3.material.io/components/sliders/specs) · [Guidelines](https://m3.material.io/components/sliders/guidelines) · [Accessibility](https://m3.material.io/components/sliders/accessibility) |
| Snackbar | [Overview](https://m3.material.io/components/snackbar/overview) · [Specs](https://m3.material.io/components/snackbar/specs) · [Guidelines](https://m3.material.io/components/snackbar/guidelines) · [Accessibility](https://m3.material.io/components/snackbar/accessibility) |
| Switch | [Overview](https://m3.material.io/components/switch/overview) · [Specs](https://m3.material.io/components/switch/specs) · [Guidelines](https://m3.material.io/components/switch/guidelines) · [Accessibility](https://m3.material.io/components/switch/accessibility) |
| Tabs | [Overview](https://m3.material.io/components/tabs/overview) · [Specs](https://m3.material.io/components/tabs/specs) · [Guidelines](https://m3.material.io/components/tabs/guidelines) · [Accessibility](https://m3.material.io/components/tabs/accessibility) |
| Text fields | [Overview](https://m3.material.io/components/text-fields/overview) · [Specs](https://m3.material.io/components/text-fields/specs) · [Guidelines](https://m3.material.io/components/text-fields/guidelines) · [Accessibility](https://m3.material.io/components/text-fields/accessibility) |
| Toolbars | [Overview](https://m3.material.io/components/toolbars/overview) · [Specs](https://m3.material.io/components/toolbars/specs) · [Guidelines](https://m3.material.io/components/toolbars/guidelines) · [Accessibility](https://m3.material.io/components/toolbars/accessibility) |
| Tooltips | [Overview](https://m3.material.io/components/tooltips/overview) · [Specs](https://m3.material.io/components/tooltips/specs) · [Guidelines](https://m3.material.io/components/tooltips/guidelines) · [Accessibility](https://m3.material.io/components/tooltips/accessibility) |

---

## Глоссарий

| Термин | Значение |
|---|---|
| **M3 Expressive** | Развитие Material 3, включающее выразительные компоненты, стили и приёмы дизайна. |
| **Design token** | Именованное значение или ссылка на другое значение дизайна. |
| **HCT** | Цветовая модель, описывающая оттенок, цветность и тон. |
| **Dynamic color** | Схема, полученная из системного или контентного источника. |
| **State layer** | Полупрозрачный слой, обозначающий состояние взаимодействия. |
| **Shape morph** | Анимированное изменение формы. |
| **Motion scheme** | Набор спецификаций движения, используемый семантическими токенами. |
| **Emphasized type** | Версия типографического стиля с дополнительным акцентом. |
| **Breakpoint** | Порог размера окна для адаптации компоновки. |
| **Scaffold** | Общая структура экрана из bars, rails и panes. |
| **Pane** | Самостоятельная область основного или связанного контента. |
| **Slot** | Предусмотренная компонентом область для сменного содержимого. |

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
