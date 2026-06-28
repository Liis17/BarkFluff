package com.barkfluff.clientv2.ui.screens.login

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.barkfluff.clientv2.di.appViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LoginScreen(onLoggedIn: () -> Unit) {
    val vm = appViewModel { LoginViewModel(it.appContext, it.grpcManager, it.globalParam) }
    val state by vm.ui.collectAsStateWithLifecycle()

    var login by rememberSaveable { mutableStateOf("") }
    var password by rememberSaveable { mutableStateOf("") }
    var otp by rememberSaveable { mutableStateOf("") }

    Scaffold(
        modifier = Modifier.fillMaxSize(),
        topBar = { TopAppBar(title = { Text(if (state.otpMode) "Подтверждение" else "Вход") }) }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(horizontal = 24.dp, vertical = 16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            if (!state.otpMode) {
                OutlinedTextField(
                    value = login,
                    onValueChange = { login = it },
                    label = { Text("Имя пользователя или email") },
                    singleLine = true,
                    enabled = !state.loading,
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = password,
                    onValueChange = { password = it },
                    label = { Text("Пароль") },
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                    enabled = !state.loading,
                    modifier = Modifier.fillMaxWidth()
                )
            } else {
                Text(
                    text = "Введите 6-значный код двухфакторной аутентификации",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                OutlinedTextField(
                    value = otp,
                    onValueChange = { if (it.length <= 6 && it.all { c -> c.isDigit() }) otp = it },
                    label = { Text("Код") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    enabled = !state.loading,
                    modifier = Modifier.fillMaxWidth()
                )
            }

            state.error?.let { error ->
                Text(
                    text = error,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodyMedium
                )
            }

            Button(
                onClick = {
                    if (state.otpMode) vm.submitOtp(otp, onLoggedIn)
                    else vm.submitCredentials(login, password, onLoggedIn)
                },
                enabled = !state.loading && if (state.otpMode) otp.length == 6 else (login.isNotBlank() && password.isNotBlank()),
                modifier = Modifier
                    .fillMaxWidth()
                    .height(56.dp),
                shape = MaterialTheme.shapes.large
            ) {
                if (state.loading) {
                    CircularProgressIndicator(
                        modifier = Modifier.height(24.dp),
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                } else {
                    Text(if (state.otpMode) "Подтвердить" else "Войти")
                }
            }
        }
    }
}
