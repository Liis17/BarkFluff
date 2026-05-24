package com.barkfluff.clientv2.ui.screens.settings

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LargeTopAppBar
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.barkfluff.clientv2.di.LocalAppContainer
import com.barkfluff.clientv2.di.ThemeMode
import com.barkfluff.clientv2.ui.components.SettingsRow
import com.barkfluff.clientv2.ui.components.SettingsSection

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(onBack: () -> Unit, onOpenPrivacy: () -> Unit, onOpenSecurity: () -> Unit) {
    val settings = LocalAppContainer.current.settingsStore
    val context = LocalContext.current
    val themeMode by settings.themeMode.collectAsStateWithLifecycle()
    val dynamicColor by settings.dynamicColor.collectAsStateWithLifecycle()
    val scrollBehavior = TopAppBarDefaults.exitUntilCollapsedScrollBehavior()

    val version = remember {
        runCatching { context.packageManager.getPackageInfo(context.packageName, 0).versionName }
            .getOrNull() ?: "—"
    }

    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        topBar = {
            LargeTopAppBar(
                title = { Text("Настройки") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Назад")
                    }
                },
                scrollBehavior = scrollBehavior
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(vertical = 8.dp)
        ) {
            SettingsSection(title = "Оформление") {
                ThemeOption("Системная", ThemeMode.SYSTEM, themeMode, settings::setThemeMode)
                ThemeOption("Светлая", ThemeMode.LIGHT, themeMode, settings::setThemeMode)
                ThemeOption("Тёмная", ThemeMode.DARK, themeMode, settings::setThemeMode)
                SettingsRow(
                    title = "Динамические цвета",
                    subtitle = "Палитра из обоев системы (Material You)",
                    trailing = { Switch(checked = dynamicColor, onCheckedChange = settings::setDynamicColor) }
                )
            }

            SettingsSection {
                SettingsRow(title = "Конфиденциальность", onClick = onOpenPrivacy)
                SettingsRow(
                    title = "Безопасность",
                    subtitle = "Пароль и двухфакторная аутентификация",
                    onClick = onOpenSecurity
                )
            }

            SettingsSection(title = "О приложении") {
                SettingsRow(
                    title = "Версия",
                    trailing = { Text(version, color = MaterialTheme.colorScheme.onSurfaceVariant) }
                )
            }

            Spacer(Modifier.size(16.dp))
        }
    }
}

@Composable
private fun ThemeOption(
    label: String,
    mode: ThemeMode,
    current: ThemeMode,
    onSelect: (ThemeMode) -> Unit,
) {
    SettingsRow(
        title = label,
        leading = { RadioButton(selected = current == mode, onClick = { onSelect(mode) }) },
        onClick = { onSelect(mode) }
    )
}
