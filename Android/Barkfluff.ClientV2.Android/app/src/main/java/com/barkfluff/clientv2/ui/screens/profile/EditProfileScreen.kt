package com.barkfluff.clientv2.ui.screens.profile

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.barkfluff.clientv2.di.appViewModel
import com.barkfluff.clientv2.ui.components.BfAvatar

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun EditProfileScreen(onBack: () -> Unit, onSaved: () -> Unit) {
    val vm = appViewModel { EditProfileViewModel(it.appContext, it.grpcManager, it.globalParam) }
    val state by vm.ui.collectAsStateWithLifecycle()

    LaunchedEffect(state.done) { if (state.done) onSaved() }

    val avatarPicker = rememberLauncherForActivityResult(
        ActivityResultContracts.PickVisualMedia()
    ) { uri -> if (uri != null) vm.pickAvatar(uri) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Редактирование") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Назад")
                    }
                },
                actions = {
                    TextButton(onClick = vm::save, enabled = !state.saving) {
                        Text("Сохранить")
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
                .padding(horizontal = 24.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Spacer(Modifier.size(16.dp))
            Box(contentAlignment = Alignment.Center) {
                BfAvatar(
                    name = "${state.firstName} ${state.lastName}".trim().ifBlank { state.username }.ifBlank { "?" },
                    size = 112.dp,
                    imageUrl = state.avatarUrl,
                    modifier = Modifier.clickable {
                        avatarPicker.launch(
                            PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)
                        )
                    }
                )
                if (state.uploadingAvatar) {
                    CircularProgressIndicator(modifier = Modifier.size(40.dp))
                }
            }
            Text(
                text = "Сменить фото",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.primary,
                modifier = Modifier.padding(top = 8.dp).clickable {
                    avatarPicker.launch(
                        PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)
                    )
                }
            )

            Spacer(Modifier.size(24.dp))
            OutlinedTextField(
                value = state.firstName,
                onValueChange = vm::setFirstName,
                label = { Text("Имя") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.size(12.dp))
            OutlinedTextField(
                value = state.lastName,
                onValueChange = vm::setLastName,
                label = { Text("Фамилия") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.size(12.dp))
            val usernameHint = when (state.usernameStatus) {
                UsernameStatus.TAKEN -> "Имя пользователя занято"
                UsernameStatus.INVALID -> "3–32 символа: латинские буквы, цифры, _"
                UsernameStatus.AVAILABLE -> "Доступно"
                else -> null
            }
            OutlinedTextField(
                value = state.username,
                onValueChange = vm::setUsername,
                label = { Text("Имя пользователя") },
                prefix = { Text("@") },
                singleLine = true,
                isError = state.usernameStatus == UsernameStatus.TAKEN ||
                    state.usernameStatus == UsernameStatus.INVALID,
                trailingIcon = {
                    when (state.usernameStatus) {
                        UsernameStatus.CHECKING -> CircularProgressIndicator(
                            modifier = Modifier.size(20.dp), strokeWidth = 2.dp
                        )
                        UsernameStatus.AVAILABLE -> Icon(
                            Icons.Filled.Check, contentDescription = null,
                            tint = MaterialTheme.colorScheme.primary
                        )
                        else -> {}
                    }
                },
                supportingText = usernameHint?.let { hint -> { Text(hint) } },
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.size(12.dp))
            OutlinedTextField(
                value = state.bio,
                onValueChange = vm::setBio,
                label = { Text("О себе") },
                minLines = 3,
                modifier = Modifier.fillMaxWidth()
            )

            if (state.error != null) {
                Spacer(Modifier.size(12.dp))
                Text(
                    text = state.error!!,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodyMedium,
                    textAlign = TextAlign.Center
                )
            }

            if (state.saving) {
                Spacer(Modifier.size(16.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    CircularProgressIndicator(modifier = Modifier.size(20.dp), strokeWidth = 2.dp)
                    Spacer(Modifier.width(8.dp))
                    Text("Сохранение…", style = MaterialTheme.typography.bodyMedium)
                }
            }
            Spacer(Modifier.size(24.dp))
        }
    }
}
