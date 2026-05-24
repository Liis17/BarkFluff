package com.barkfluff.clientv2.ui.screens.settings

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.barkfluff.clientv2.di.LocalAppContainer
import com.barkfluff.clientv2.di.ThemeMode

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(onBack: () -> Unit) {
    val settings = LocalAppContainer.current.settingsStore
    val context = LocalContext.current
    val themeMode by settings.themeMode.collectAsStateWithLifecycle()
    val dynamicColor by settings.dynamicColor.collectAsStateWithLifecycle()

    val version = remember {
        runCatching { context.packageManager.getPackageInfo(context.packageName, 0).versionName }
            .getOrNull() ?: "—"
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Настройки") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Назад")
                    }
                }
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
        ) {
            SectionHeader("Оформление")
            ThemeOption("Системная", ThemeMode.SYSTEM, themeMode, settings::setThemeMode)
            ThemeOption("Светлая", ThemeMode.LIGHT, themeMode, settings::setThemeMode)
            ThemeOption("Тёмная", ThemeMode.DARK, themeMode, settings::setThemeMode)
            ListItem(
                headlineContent = { Text("Динамические цвета") },
                supportingContent = { Text("Палитра из обоев системы (Material You)") },
                trailingContent = {
                    Switch(checked = dynamicColor, onCheckedChange = settings::setDynamicColor)
                }
            )

            HorizontalDivider()
            SectionHeader("О приложении")
            ListItem(
                headlineContent = { Text("Версия") },
                trailingContent = { Text(version, color = MaterialTheme.colorScheme.onSurfaceVariant) }
            )
        }
    }
}

@Composable
private fun SectionHeader(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.labelLarge,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(start = 16.dp, top = 16.dp, bottom = 4.dp)
    )
}

@Composable
private fun ThemeOption(
    label: String,
    mode: ThemeMode,
    current: ThemeMode,
    onSelect: (ThemeMode) -> Unit,
) {
    ListItem(
        headlineContent = { Text(label) },
        leadingContent = {
            RadioButton(selected = current == mode, onClick = { onSelect(mode) })
        },
        modifier = Modifier.selectable(selected = current == mode) { onSelect(mode) }
    )
}
