package com.barkfluff.clientv2.ui.screens.settings

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import barkfluff.users.UsersApiOuterClass.ProfileFieldVisibility
import com.barkfluff.clientv2.di.appViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PrivacyScreen(onBack: () -> Unit) {
    val vm = appViewModel { PrivacyViewModel(it.grpcManager) }
    val state by vm.ui.collectAsStateWithLifecycle()
    val s = state.settings

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Конфиденциальность") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Назад")
                    }
                }
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
        ) {
            ListItem(
                headlineContent = { Text("Профиль виден на сайте") },
                supportingContent = { Text("Публичная страница barkfluff.com") },
                trailingContent = {
                    Switch(checked = s.profileVisibleOnSite, onCheckedChange = vm::setProfileVisibleOnSite)
                }
            )
            ListItem(
                headlineContent = { Text("Показывать в поиске") },
                trailingContent = {
                    Switch(checked = s.searchVisible, onCheckedChange = vm::setSearchVisible)
                }
            )

            HorizontalDivider()
            Text(
                text = "Видимость",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.primary,
                modifier = Modifier.padding(start = 16.dp, top = 16.dp, bottom = 4.dp)
            )
            VisibilityRow("Аватар", s.avatarVisibility, vm::setAvatarVisibility)
            VisibilityRow("Описание", s.bioVisibility, vm::setBioVisibility)
            VisibilityRow("Почта", s.emailVisibility, vm::setEmailVisibility)
            VisibilityRow("Онлайн-статус", s.onlineVisibility, vm::setOnlineVisibility)
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
    ListItem(
        headlineContent = { Text(label) },
        trailingContent = {
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
