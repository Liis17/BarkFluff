package com.barkfluff.clientv2.ui.screens.profile

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.barkfluff.clientv2.di.LocalAppContainer
import com.barkfluff.clientv2.di.appViewModel
import com.barkfluff.clientv2.ui.components.BfAvatar
import com.barkfluff.clientv2.ui.components.SettingsRow
import com.barkfluff.clientv2.ui.components.SettingsSection
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ProfileScreen(onEditProfile: () -> Unit, onOpenSettings: () -> Unit, onLogout: () -> Unit) {
    val container = LocalAppContainer.current
    val vm = appViewModel { ProfileViewModel(it.grpcManager, it.globalParam) }
    val state by vm.ui.collectAsStateWithLifecycle()

    // Подтягиваем свежие данные при каждом входе на экран (в т.ч. после редактирования).
    LaunchedEffect(Unit) { vm.refresh() }

    Scaffold(topBar = { TopAppBar(title = { Text("Профиль") }) }) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState()),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Spacer(Modifier.size(16.dp))
            BfAvatar(name = state.fullName.ifBlank { "?" }, size = 112.dp, imageUrl = state.avatarUrl)
            Spacer(Modifier.size(12.dp))
            Text(
                text = state.fullName.ifBlank { "Профиль" },
                style = MaterialTheme.typography.headlineSmall
            )
            if (state.username.isNotBlank()) {
                Text(
                    text = "@${state.username}",
                    style = MaterialTheme.typography.bodyLarge,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            if (state.bio.isNotBlank()) {
                Text(
                    text = state.bio,
                    style = MaterialTheme.typography.bodyMedium,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 32.dp, vertical = 8.dp)
                )
            }
            if (state.registrationDate > 0) {
                Text(
                    text = "В BarkFluff с " + dateFormat.format(Date(state.registrationDate)),
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            Spacer(Modifier.size(24.dp))
            SettingsSection {
                SettingsRow(
                    title = "Редактировать профиль",
                    leading = { Icon(Icons.Filled.Edit, contentDescription = null) },
                    onClick = onEditProfile
                )
                SettingsRow(
                    title = "Настройки",
                    leading = { Icon(Icons.Filled.Settings, contentDescription = null) },
                    onClick = onOpenSettings
                )
            }

            Spacer(Modifier.size(16.dp))
            OutlinedButton(
                onClick = {
                    container.globalParam.clearUserData()
                    onLogout()
                },
                modifier = Modifier.fillMaxWidth().padding(horizontal = 24.dp)
            ) {
                Text("Выйти")
            }
            Spacer(Modifier.size(24.dp))
        }
    }
}

private val dateFormat = SimpleDateFormat("d MMMM yyyy", Locale.getDefault())
