package com.barkfluff.clientv2.ui.theme

import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.SpringSpec
import androidx.compose.animation.core.spring

// M3 Expressive spring-движение. Spatial — для перемещений (с лёгким overshoot),
// effect — для прозрачности/цвета (без overshoot). Используется в анимациях экранов.
object BfMotion {
    fun <T> spatialDefault(): SpringSpec<T> =
        spring(dampingRatio = 0.9f, stiffness = Spring.StiffnessMedium)

    fun <T> spatialFast(): SpringSpec<T> =
        spring(dampingRatio = 0.9f, stiffness = Spring.StiffnessMediumLow * 4)

    fun <T> effectDefault(): SpringSpec<T> =
        spring(dampingRatio = 1f, stiffness = Spring.StiffnessHigh)

    fun <T> bouncy(): SpringSpec<T> =
        spring(dampingRatio = Spring.DampingRatioMediumBouncy, stiffness = Spring.StiffnessMedium)
}
