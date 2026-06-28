package com.barkfluff.clientv2.di

import androidx.compose.runtime.Composable
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory

/**
 * Создаёт [ViewModel] с доступом к [AppContainer] (ручной DI). Заменяет Hilt для MVP-масштаба.
 *
 * Пример: `val vm = appViewModel { LoginViewModel(it.appContext, it.grpcManager, it.globalParam) }`
 */
@Composable
inline fun <reified VM : ViewModel> appViewModel(
    crossinline create: (AppContainer) -> VM
): VM {
    val container = LocalAppContainer.current
    return viewModel(factory = viewModelFactory { initializer { create(container) } })
}
