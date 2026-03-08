# Material 3 Expressive Migration Summary

## Обзор изменений

Проект BarkFluff.Client.Android был обновлён до **Material 3 Expressive** (2025–2026) с поддержкой Android 16 (API 36).

---

## Основные изменения

### 1. Зависимости (build.gradle.kts)

Добавлены:
- **Jetpack Compose BOM** (2024.12.01) для современной UI-разработки
- **Material 3** (`androidx.compose.material3:material3`)
- **Material Icons Extended**
- **Activity Compose** и **Lifecycle ViewModel Compose**
- **Navigation Compose**
- **Coil Compose** для загрузки изображений

### 2. Темы и цвета

#### themes.xml
- Включена поддержка **Material 3 Expressive** токенов
- Настроены **shape appearance** для компонентов (Small, Medium, Large)
- Включена **emphasized типографика** для заголовков
- Настроена поддержка **edge-to-edge** режима

#### colors.xml (Light & Dark)
Полная переработка цветовой схемы с использованием **26+ семантических ролей**:
- `primary`, `on_primary`, `primary_container`, `on_primary_container`
- `secondary`, `on_secondary`, `secondary_container`
- `tertiary`, `on_tertiary`, `tertiary_container`
- `error`, `error_container`
- `surface`, `on_surface`, `surface_container_*` (5 уровней)
- `outline`, `outline_variant`
- `inverse_surface`, `inverse_on_surface`, `inverse_primary`
- `success`, `warning` цвета

### 3. Формы и радиусы (Material 3 Expressive)

Новые токены в `dimens.xml`:
- **Extra Extra Large**: 48dp (hero элементы)
- **Extra Large**: 32dp (крупные карточки)
- **Large**: 20dp (карточки и FAB)
- **Medium**: 16dp (поля ввода и кнопки)
- **Small**: 12dp (маленькие элементы)
- **Full**: 999dp (круглые элементы)

### 4. Layout-файлы

#### activity_welcome.xml
- Hero секция с градиентом
- Логотип в карточке с радиусом 35dp
- Emphasized типографика (Display Medium, Headline Small)
- Кнопки с морфингом (радиус 32dp)
- Tonal кнопка "Узнать больше"

#### activity_login.xml
- Увеличенные радиусы (24dp для карточек)
- OTP boxes с Material 3 TextInputLayout
- Error карточка с colorErrorContainer
- Кнопки высотой 64dp

#### activity_select_server.xml
- Карточки серверов с увеличенными радиусами (20dp)
- Информационная панель внизу
- Кнопки с иконками

#### activity_chats.xml
- Material 3 AppBar с liftOnScroll
- Поле поиска с радиусом 24dp
- FAB для нового чата
- Пустое состояние с иконкой в карточке

#### layout_drawer.xml
- Профиль с полной шириной
- Menu items как TextButton (56dp высота)
- Разделители с MaterialDivider
- Секции с заголовками

#### item_chat.xml
- Аватар 60dp с полным скруглением
- Unread badge в карточке с радиусом 12dp
- Emphasized типографика

#### item_server.xml
- Цветовой индикатор с радиусом 3dp
- Карточка с радиусом 20dp
- Иконка навигации в контейнере

#### activity_splash.xml
- Минималистичный дизайн
- Логотип в карточке (40dp радиус)
- Градиентный фон

### 5. Шаги регистрации

Все 9 шагов регистрации обновлены:
- **step_register_01_name.xml**: Личная информация с подсказками
- **step_register_02_username.xml**: Имя пользователя с префиксом @
- **step_register_03_email.xml**: Email с иконками
- **step_register_04_verify.xml**: Код подтверждения с карточками
- **step_register_05_password.xml**: Пароль с индикатором надёжности
- **step_register_06_avatar.xml**: Фото в круглой карточке
- **step_register_07_bio.xml**: Профиль preview + bio
- **step_register_08_2fa.xml**: 2FA с информационными карточками
- **step_register_09_complete.xml**: Завершение с иконкой

### 6. Drawable ресурсы

- **otp_box_background.xml**: Радиус 16dp, stroke 2dp
- **unread_badge_background.xml**: Использует ?attr/colorPrimary
- **online_indicator.xml**: Обновлён для тёмной темы
- **background_color_indicator.xml**: Радиус 3dp
- **hero_gradient_background.xml**: Новый градиент

### 7. Activity Kotlin файлы

#### WelcomeActivity.kt
- Удалены анимации (заменены на статичный дизайн)
- Добавлена поддержка edge-to-edge
- Добавлена кнопка "Узнать больше"

#### ChatsActivity.kt
- Добавлен обработчик FAB

#### RegisterActivity.kt
- Требует обновления для работы с новыми binding

### 8. Типографика

Использованы стили Material 3:
- `textAppearanceDisplayMedium` для заголовков
- `textAppearanceHeadlineMedium/Large` для подзаголовков
- `textAppearanceTitleMedium` для кнопок
- `textAppearanceBodyLarge/Medium` для текста
- `textAppearanceLabelSmall` для подписей

### 9. Компоненты

- **MaterialButton**: Высота 56dp/64dp, радиус 32dp
- **TextInputLayout**: OutlinedBox с радиусом 16dp
- **MaterialCardView**: Радиусы 20-24dp, elevation 0dp
- **LinearProgressIndicator**: Толщина 6dp
- **CircularProgressIndicator**: Material 3 стиль
- **FloatingActionButton**: Normal size, 20dp margin
- **MaterialDivider**: Для разделителей в меню

---

## Рекомендации по тестированию

1. **Сборка**: `./gradlew assembleDebug`
2. **Тестирование на Android 16** (API 36) эмуляторе
3. **Проверка тёмной темы**
4. **Проверка dynamic colors** (на Pixel устройствах)
5. **Тестирование на больших экранах** (≥600dp)

---

## Известные проблемы

1. **RegisterActivity.kt** требует обновления для работы с новыми binding переменными
2. Некоторые view могут требовать обновления ID после изменения layout

---

## Следующие шаги

1. Обновить RegisterActivity.kt для работы с новыми binding
2. Добавить physics-based анимации (springs)
3. Добавить haptic feedback при жестах
4. Реализовать adaptive layouts для больших экранов
5. Добавить blur backgrounds для sheets

---

## Источники

- [Material 3 Expressive Guidelines](https://m3.material.io/)
- [Android 16 Developer Preview](https://developer.android.com/about/versions/16)
- [Jetpack Compose Documentation](https://developer.android.com/jetpack/compose)
