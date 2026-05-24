package com.barkfluff.clientv2.di

import androidx.compose.runtime.staticCompositionLocalOf

/** Доступ к [AppContainer] из любого Composable. */
val LocalAppContainer = staticCompositionLocalOf<AppContainer> {
    error("AppContainer не предоставлен. Оберните дерево в CompositionLocalProvider(LocalAppContainer provides ...).")
}
