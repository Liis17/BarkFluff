package com.barkfluff.messenger.presentation.screens.auth

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.barkfluff.messenger.R
import com.barkfluff.messenger.presentation.navigation.Screen
import com.barkfluff.messenger.presentation.viewmodel.AuthEvent
import com.barkfluff.messenger.presentation.viewmodel.AuthViewModel
import kotlinx.coroutines.flow.collectLatest

@Composable
fun VerifyEmailScreen(
    navController: NavController,
    email: String,
    viewModel: AuthViewModel = hiltViewModel()
) {
    var code by remember { mutableStateOf("") }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    val state by viewModel.state.collectAsState()

    LaunchedEffect(Unit) {
        viewModel.events.collectLatest { event ->
            when (event) {
                is AuthEvent.ConfirmSuccess -> {
                    navController.navigate(Screen.ChatList.route) {
                        popUpTo(Screen.Welcome.route) { inclusive = true }
                    }
                }
                is AuthEvent.Error -> {
                    errorMessage = event.message
                }
                else -> {}
            }
        }
    }

    Surface(
        modifier = Modifier.fillMaxSize(),
        color = MaterialTheme.colorScheme.background
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(32.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center
        ) {
            Text(
                text = "Проверьте почту",
                style = MaterialTheme.typography.headlineMedium,
                textAlign = TextAlign.Center
            )

            Spacer(modifier = Modifier.height(16.dp))

            Text(
                text = "Мы отправили код подтверждения на $email",
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.onBackground.copy(alpha = 0.7f),
                textAlign = TextAlign.Center
            )

            Spacer(modifier = Modifier.height(32.dp))

            errorMessage?.let {
                Text(
                    text = it,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodyMedium,
                    modifier = Modifier.padding(bottom = 16.dp)
                )
            }

            OutlinedTextField(
                value = code,
                onValueChange = { code = it },
                label = { Text(stringResource(R.string.verification_code)) },
                modifier = Modifier.fillMaxWidth(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true
            )

            Spacer(modifier = Modifier.height(24.dp))

            Button(
                onClick = { viewModel.confirmAccount(email, code) },
                modifier = Modifier
                    .fillMaxWidth()
                    .height(56.dp),
                enabled = !state.isLoading && code.isNotBlank()
            ) {
                if (state.isLoading) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(24.dp),
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                } else {
                    Text(
                        text = stringResource(R.string.verify),
                        style = MaterialTheme.typography.titleMedium
                    )
                }
            }

            Spacer(modifier = Modifier.height(16.dp))

            TextButton(onClick = { /* TODO: Resend code */ }) {
                Text(stringResource(R.string.resend_code))
            }
        }
    }
}
