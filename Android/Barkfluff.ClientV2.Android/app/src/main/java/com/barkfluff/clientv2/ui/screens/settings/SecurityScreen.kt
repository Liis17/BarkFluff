package com.barkfluff.clientv2.ui.screens.settings

import android.graphics.BitmapFactory
import android.util.Base64
import android.widget.Toast
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LargeTopAppBar
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import barkfluff.identity.IdentityApiOuterClass.OtpTypeId
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.clientv2.di.appViewModel
import com.barkfluff.clientv2.ui.components.SettingsRow
import com.barkfluff.clientv2.ui.components.SettingsSection

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SecurityScreen(onBack: () -> Unit) {
    val vm = appViewModel { SecurityViewModel(it.grpcManager) }
    val state by vm.ui.collectAsStateWithLifecycle()
    val context = LocalContext.current

    var showPasswordDialog by remember { mutableStateOf(false) }
    var otpSetup by remember { mutableStateOf<GrpcManager.OtpSetupResult?>(null) }
    var disableTarget by remember { mutableStateOf<OtpTypeId?>(null) }

    fun toast(text: String) = Toast.makeText(context, text, Toast.LENGTH_SHORT).show()

    val scrollBehavior = TopAppBarDefaults.exitUntilCollapsedScrollBehavior()

    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        topBar = {
            LargeTopAppBar(
                title = { Text("Безопасность") },
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
                SettingsRow(title = "Сменить пароль", onClick = { showPasswordDialog = true })
            }
            SettingsSection(title = "Двухфакторная аутентификация") {
                SettingsRow(
                    title = "Приложение-аутентификатор",
                    subtitle = "Коды из Google Authenticator и подобных",
                    trailing = {
                        Switch(
                            checked = state.authenticatorEnabled,
                            onCheckedChange = { enable ->
                                if (enable) {
                                    vm.beginAuthenticatorSetup { result ->
                                        result
                                            .onSuccess { otpSetup = it }
                                            .onFailure { toast(it.message ?: "Не удалось начать настройку") }
                                    }
                                } else {
                                    disableTarget = OtpTypeId.Authenticator
                                }
                            }
                        )
                    }
                )
                SettingsRow(
                    title = "Подтверждение по почте",
                    subtitle = "Код приходит на привязанную почту",
                    trailing = {
                        Switch(
                            checked = state.emailEnabled,
                            onCheckedChange = { enable ->
                                if (enable) {
                                    vm.enableEmail { result ->
                                        result
                                            .onSuccess { toast("2FA по почте включена") }
                                            .onFailure { toast(it.message ?: "Не удалось включить") }
                                    }
                                } else {
                                    disableTarget = OtpTypeId.Email
                                }
                            }
                        )
                    }
                )
            }
            Spacer(Modifier.size(16.dp))
        }
    }

    if (showPasswordDialog) {
        ChangePasswordDialog(
            submit = vm::changePassword,
            onSuccess = {
                showPasswordDialog = false
                toast("Пароль изменён")
            },
            onDismiss = { showPasswordDialog = false }
        )
    }

    otpSetup?.let { setup ->
        AuthenticatorSetupDialog(
            setup = setup,
            submit = vm::confirmAuthenticator,
            onSuccess = {
                otpSetup = null
                toast("Аутентификатор подключён")
            },
            onDismiss = { otpSetup = null }
        )
    }

    disableTarget?.let { target ->
        DisableOtpDialog(
            submit = { code, onResult -> vm.disable(target, code, onResult) },
            onSuccess = {
                disableTarget = null
                toast("2FA отключена")
            },
            onDismiss = { disableTarget = null }
        )
    }
}

@Composable
private fun ChangePasswordDialog(
    submit: (old: String, new: String, onResult: (Result<Unit>) -> Unit) -> Unit,
    onSuccess: () -> Unit,
    onDismiss: () -> Unit,
) {
    var old by remember { mutableStateOf("") }
    var newPassword by remember { mutableStateOf("") }
    var confirm by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Смена пароля") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = old, onValueChange = { old = it },
                    label = { Text("Текущий пароль") }, singleLine = true,
                    visualTransformation = PasswordVisualTransformation()
                )
                OutlinedTextField(
                    value = newPassword, onValueChange = { newPassword = it },
                    label = { Text("Новый пароль") }, singleLine = true,
                    visualTransformation = PasswordVisualTransformation()
                )
                OutlinedTextField(
                    value = confirm, onValueChange = { confirm = it },
                    label = { Text("Повторите новый") }, singleLine = true,
                    visualTransformation = PasswordVisualTransformation()
                )
                error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            TextButton(
                enabled = !busy,
                onClick = {
                    when {
                        newPassword.isBlank() -> error = "Введите новый пароль"
                        newPassword != confirm -> error = "Пароли не совпадают"
                        else -> {
                            busy = true
                            error = null
                            submit(old, newPassword) { result ->
                                busy = false
                                result.onSuccess { onSuccess() }
                                    .onFailure { error = it.message ?: "Ошибка" }
                            }
                        }
                    }
                }
            ) { Text("Сменить") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Отмена") } }
    )
}

@Composable
private fun AuthenticatorSetupDialog(
    setup: GrpcManager.OtpSetupResult,
    submit: (code: String, onResult: (Result<Unit>) -> Unit) -> Unit,
    onSuccess: () -> Unit,
    onDismiss: () -> Unit,
) {
    var code by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    val qr = remember(setup.qrBase64) { decodeBase64ToImageBitmap(setup.qrBase64) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Подключение аутентификатора") },
        text = {
            Column(
                verticalArrangement = Arrangement.spacedBy(8.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                if (qr != null) {
                    Image(bitmap = qr, contentDescription = "QR-код", modifier = Modifier.size(200.dp))
                }
                if (setup.justCode.isNotBlank()) {
                    Text("Или введите ключ вручную:", style = MaterialTheme.typography.bodySmall)
                    Text(setup.justCode, style = MaterialTheme.typography.titleMedium)
                }
                OutlinedTextField(
                    value = code, onValueChange = { code = it },
                    label = { Text("Код из приложения") }, singleLine = true
                )
                error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            TextButton(
                enabled = !busy,
                onClick = {
                    if (code.isBlank()) {
                        error = "Введите код"
                    } else {
                        busy = true
                        error = null
                        submit(code) { result ->
                            busy = false
                            result.onSuccess { onSuccess() }
                                .onFailure { error = it.message ?: "Неверный код" }
                        }
                    }
                }
            ) { Text("Подтвердить") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Отмена") } }
    )
}

@Composable
private fun DisableOtpDialog(
    submit: (code: String, onResult: (Result<Unit>) -> Unit) -> Unit,
    onSuccess: () -> Unit,
    onDismiss: () -> Unit,
) {
    var code by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Отключение 2FA") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Введите текущий код подтверждения, чтобы отключить.")
                OutlinedTextField(
                    value = code, onValueChange = { code = it },
                    label = { Text("Код") }, singleLine = true
                )
                error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            TextButton(
                enabled = !busy,
                onClick = {
                    if (code.isBlank()) {
                        error = "Введите код"
                    } else {
                        busy = true
                        error = null
                        submit(code) { result ->
                            busy = false
                            result.onSuccess { onSuccess() }
                                .onFailure { error = it.message ?: "Неверный код" }
                        }
                    }
                }
            ) { Text("Отключить") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Отмена") } }
    )
}

private fun decodeBase64ToImageBitmap(base64: String): ImageBitmap? = try {
    val clean = if (base64.contains("base64,")) base64.substringAfter("base64,") else base64
    val bytes = Base64.decode(clean, Base64.DEFAULT)
    BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
} catch (_: Exception) {
    null
}
