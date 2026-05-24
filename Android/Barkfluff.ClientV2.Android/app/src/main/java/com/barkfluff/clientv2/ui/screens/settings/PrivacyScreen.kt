package com.barkfluff.clientv2.ui.screens.settings

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LargeTopAppBar
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import barkfluff.users.UsersApiOuterClass.ProfileFieldVisibility
import com.barkfluff.clientv2.di.appViewModel
import com.barkfluff.clientv2.ui.components.SettingsRow
import com.barkfluff.clientv2.ui.components.SettingsSection

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PrivacyScreen(onBack: () -> Unit) {
    val vm = appViewModel { PrivacyViewModel(it.grpcManager) }
    val state by vm.ui.collectAsStateWithLifecycle()
    val s = state.settings
    val scrollBehavior = TopAppBarDefaults.exitUntilCollapsedScrollBehavior()

    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        topBar = {
            LargeTopAppBar(
                title = { Text("Конфиденциальность") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Назад")
                    }
                },
                scrollBehavior = scrollBehavior
            )
        }
    ) { padding ->
        if (state.loading) {
            Box(Modifier.fillMaxSize().padding(padding), contentAlignment = Alignment.Center) {
                CircularProgressIndicator()
            }
            return@Scaffold
        }
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(vertical = 8.dp)
        ) {
            SettingsSection {
                SettingsRow(
                    title = "Профиль виден на сайте",
                    subtitle = "Публичная страница barkfluff.com",
                    trailing = {
                        Switch(checked = s.profileVisibleOnSite, onCheckedChange = vm::setProfileVisibleOnSite)
                    }
                )
                SettingsRow(
                    title = "Показывать в поиске",
                    trailing = { Switch(checked = s.searchVisible, onCheckedChange = vm::setSearchVisible) }
                )
            }

            SettingsSection(title = "Видимость") {
                VisibilityRow("Аватар", s.avatarVisibility, vm::setAvatarVisibility)
                VisibilityRow("Описание", s.bioVisibility, vm::setBioVisibility)
                VisibilityRow("Почта", s.emailVisibility, vm::setEmailVisibility)
                VisibilityRow("Онлайн-статус", s.onlineVisibility, vm::setOnlineVisibility)
            }

            Spacer(Modifier.size(16.dp))
        }
    }
}

private val visibilityOptions = listOf(
    ProfileFieldVisibility.ALL,
    ProfileFieldVisibility.FRIENDS,
    ProfileFieldVisibility.NONE,
)

private fun visibilityLabel(value: ProfileFieldVisibility): String = when (value) {
    ProfileFieldVisibility.ALL -> "Все"
    ProfileFieldVisibility.FRIENDS -> "Друзья"
    ProfileFieldVisibility.NONE -> "Никто"
    else -> "—"
}

@Composable
private fun VisibilityRow(
    label: String,
    value: ProfileFieldVisibility,
    onSelect: (ProfileFieldVisibility) -> Unit,
) {
    var expanded by remember { mutableStateOf(false) }
    SettingsRow(
        title = label,
        trailing = {
            Box {
                TextButton(onClick = { expanded = true }) { Text(visibilityLabel(value)) }
                DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                    visibilityOptions.forEach { option ->
                        DropdownMenuItem(
                            text = { Text(visibilityLabel(option)) },
                            onClick = {
                                onSelect(option)
                                expanded = false
                            }
                        )
                    }
                }
            }
        }
    )
}
