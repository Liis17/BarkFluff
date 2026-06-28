package com.barkfluff.clientv2.ui.theme

import androidx.compose.material3.Typography
import androidx.compose.ui.text.font.FontWeight

// M3 Expressive: emphasized-веса на ключевых (коротких) стилях — display/headline/title.
// Body остаётся в baseline для читаемости длинных текстов.
private val default = Typography()

val BarkFluffTypography = default.copy(
    displayLarge = default.displayLarge.copy(fontWeight = FontWeight.Bold),
    displayMedium = default.displayMedium.copy(fontWeight = FontWeight.Bold),
    headlineLarge = default.headlineLarge.copy(fontWeight = FontWeight.Bold),
    headlineMedium = default.headlineMedium.copy(fontWeight = FontWeight.SemiBold),
    headlineSmall = default.headlineSmall.copy(fontWeight = FontWeight.SemiBold),
    titleLarge = default.titleLarge.copy(fontWeight = FontWeight.SemiBold),
)
