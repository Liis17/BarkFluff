package com.barkfluff.clientv2.ui.theme

import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color

// Цветовая схема M3 Expressive. Seed: #FF6B35 (BarkFluff Orange). Значения — из брендовой
// палитры V1 (values/colors.xml + values-night/colors.xml).

val BarkFluffLightColors = lightColorScheme(
    primary = Color(0xFFFF6B35),
    onPrimary = Color(0xFFFFFFFF),
    primaryContainer = Color(0xFFFFDAD0),
    onPrimaryContainer = Color(0xFF4A2000),
    secondary = Color(0xFF2196F3),
    onSecondary = Color(0xFFFFFFFF),
    secondaryContainer = Color(0xFFD0E4FF),
    onSecondaryContainer = Color(0xFF001F3A),
    tertiary = Color(0xFF785A0B),
    onTertiary = Color(0xFFFFFFFF),
    tertiaryContainer = Color(0xFFFFDF9C),
    onTertiaryContainer = Color(0xFF261A00),
    error = Color(0xFFBA1A1A),
    onError = Color(0xFFFFFFFF),
    errorContainer = Color(0xFFFFDAD6),
    onErrorContainer = Color(0xFF410002),
    background = Color(0xFFFEF8F6),
    onBackground = Color(0xFF1C1B1A),
    surface = Color(0xFFFEF8F6),
    onSurface = Color(0xFF1C1B1A),
    onSurfaceVariant = Color(0xFF494544),
    surfaceContainerLowest = Color(0xFFFFFFFF),
    surfaceContainerLow = Color(0xFFFEF8F6),
    surfaceContainer = Color(0xFFF0F0F0),
    surfaceContainerHigh = Color(0xFFE8E8E8),
    surfaceContainerHighest = Color(0xFFE2E2E2),
    outline = Color(0xFF7C7775),
    outlineVariant = Color(0xFFCDC6C4),
    inverseSurface = Color(0xFF31302E),
    inverseOnSurface = Color(0xFFF5F0EE),
    inversePrimary = Color(0xFFFFB5A0),
    surfaceTint = Color(0xFFFF6B35),
    scrim = Color(0xFF000000),
)

val BarkFluffDarkColors = darkColorScheme(
    primary = Color(0xFFFFB5A0),
    onPrimary = Color(0xFF5F2E00),
    primaryContainer = Color(0xFFCC551A),
    onPrimaryContainer = Color(0xFFFFDAD0),
    secondary = Color(0xFF96C9FF),
    onSecondary = Color(0xFF003255),
    secondaryContainer = Color(0xFF004C7F),
    onSecondaryContainer = Color(0xFFD0E4FF),
    tertiary = Color(0xFFF1C147),
    onTertiary = Color(0xFF3F2E00),
    tertiaryContainer = Color(0xFF5B4300),
    onTertiaryContainer = Color(0xFFFFDF9C),
    error = Color(0xFFFFB4AB),
    onError = Color(0xFF690005),
    errorContainer = Color(0xFF93000A),
    onErrorContainer = Color(0xFFFFDAD6),
    background = Color(0xFF191C1D),
    onBackground = Color(0xFFE3E2E0),
    surface = Color(0xFF191C1D),
    onSurface = Color(0xFFE3E2E0),
    onSurfaceVariant = Color(0xFFC9C4C2),
    surfaceContainerLowest = Color(0xFF121415),
    surfaceContainerLow = Color(0xFF191C1D),
    surfaceContainer = Color(0xFF2D3132),
    surfaceContainerHigh = Color(0xFF3E4142),
    surfaceContainerHighest = Color(0xFF484B4C),
    outline = Color(0xFF938F8D),
    outlineVariant = Color(0xFF494544),
    inverseSurface = Color(0xFFE3E2E0),
    inverseOnSurface = Color(0xFF31302E),
    inversePrimary = Color(0xFFCC551A),
    surfaceTint = Color(0xFFFFB5A0),
    scrim = Color(0xFF000000),
)

/** Брендовые статусные цвета (success/warning), которых нет в ролях M3. */
@Immutable
data class BfExtendedColors(
    val success: Color,
    val onSuccess: Color,
    val successContainer: Color,
    val onSuccessContainer: Color,
    val warning: Color,
    val onWarning: Color,
    val warningContainer: Color,
    val onWarningContainer: Color,
)

val LightExtendedColors = BfExtendedColors(
    success = Color(0xFF2E7D32), onSuccess = Color(0xFFFFFFFF),
    successContainer = Color(0xFFC8E6C9), onSuccessContainer = Color(0xFF1B5E20),
    warning = Color(0xFFF57C00), onWarning = Color(0xFFFFFFFF),
    warningContainer = Color(0xFFFFE0B2), onWarningContainer = Color(0xFFE65100),
)

val DarkExtendedColors = BfExtendedColors(
    success = Color(0xFF81C784), onSuccess = Color(0xFF1B5E20),
    successContainer = Color(0xFF2E7D32), onSuccessContainer = Color(0xFFC8E6C9),
    warning = Color(0xFFFFB74D), onWarning = Color(0xFFE65100),
    warningContainer = Color(0xFFF57C00), onWarningContainer = Color(0xFFFFE0B2),
)

val LocalBfExtendedColors = staticCompositionLocalOf { LightExtendedColors }
