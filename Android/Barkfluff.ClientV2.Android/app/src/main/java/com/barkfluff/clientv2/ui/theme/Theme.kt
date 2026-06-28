package com.barkfluff.clientv2.ui.theme

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.platform.LocalContext

/**
 * Тема приложения V2. Material 3 Expressive реализуется через стандартный [MaterialTheme]
 * с брендовыми expressive-токенами (цвет/типографика/форма) + [BfMotion] для движения.
 *
 * dynamicColor (Material You) доступен всегда (minSdk 35 ≥ Android 12); при false —
 * брендовая схема на основе seed #FF6B35.
 */
@Composable
fun BarkFluffTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    dynamicColor: Boolean = true,
    content: @Composable () -> Unit
) {
    val context = LocalContext.current
    val colorScheme = when {
        dynamicColor && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S ->
            if (darkTheme) dynamicDarkColorScheme(context) else dynamicLightColorScheme(context)
        darkTheme -> BarkFluffDarkColors
        else -> BarkFluffLightColors
    }
    val extended = if (darkTheme) DarkExtendedColors else LightExtendedColors

    CompositionLocalProvider(LocalBfExtendedColors provides extended) {
        MaterialTheme(
            colorScheme = colorScheme,
            typography = BarkFluffTypography,
            shapes = BarkFluffShapes,
            content = content
        )
    }
}
