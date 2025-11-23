package com.barkfluff.messenger.presentation.screens.settings

import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
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
import coil.compose.rememberAsyncImagePainter
import com.barkfluff.messenger.R
import com.barkfluff.messenger.data.repository.OtpSetupInfo
import com.barkfluff.messenger.data.repository.OtpType
import com.barkfluff.messenger.presentation.viewmodel.AuthEvent
import com.barkfluff.messenger.presentation.viewmodel.AuthViewModel
import kotlinx.coroutines.flow.collectLatest

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TwoFactorAuthScreen(
    navController: NavController,
    viewModel: AuthViewModel = hiltViewModel()
) {
    var selectedType by remember { mutableStateOf(OtpType.AUTHENTICATOR) }
    var setupInfo by remember { mutableStateOf<OtpSetupInfo?>(null) }
    var confirmCode by remember { mutableStateOf("") }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var otpMethods by remember { mutableStateOf<List<com.barkfluff.messenger.data.repository.OtpMethod>>(emptyList()) }

    val state by viewModel.state.collectAsState()

    // Load current 2FA status
    LaunchedEffect(Unit) {
        viewModel.getOtpMethods().onSuccess { methods ->
            otpMethods = methods
        }
    }

    LaunchedEffect(Unit) {
        viewModel.events.collectLatest { event ->
            when (event) {
                is AuthEvent.OtpEnabled -> {
                    setupInfo = event.setupInfo
                }
                is AuthEvent.OtpConfirmed -> {
                    navController.navigateUp()
                }
                is AuthEvent.Error -> {
                    errorMessage = event.message
                }
                else -> {}
            }
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(stringResource(R.string.two_factor_auth)) },
                navigationIcon = {
                    IconButton(onClick = { navController.navigateUp() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Back")
                    }
                }
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            // Display current 2FA status
            if (otpMethods.isNotEmpty()) {
                Card(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(bottom = 24.dp)
                ) {
                    Column(modifier = Modifier.padding(16.dp)) {
                        Text(
                            text = "Текущий статус 2FA",
                            style = MaterialTheme.typography.titleMedium,
                            modifier = Modifier.padding(bottom = 8.dp)
                        )

                        otpMethods.forEach { method ->
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 4.dp),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(
                                    text = when (method.type) {
                                        OtpType.AUTHENTICATOR -> "Authenticator"
                                        OtpType.EMAIL -> "Email"
                                    },
                                    style = MaterialTheme.typography.bodyMedium
                                )

                                Text(
                                    text = if (method.isEnabled) "Включено ✓" else "Выключено",
                                    style = MaterialTheme.typography.bodyMedium,
                                    color = if (method.isEnabled)
                                        MaterialTheme.colorScheme.primary
                                    else
                                        MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }
                }

                Divider(modifier = Modifier.padding(vertical = 16.dp))
            }

            if (setupInfo == null) {
                // Step 1: Choose 2FA type
                Text(
                    text = "Выберите метод двухфакторной аутентификации",
                    style = MaterialTheme.typography.titleMedium,
                    textAlign = TextAlign.Center
                )

                Spacer(modifier = Modifier.height(24.dp))

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(16.dp)
                ) {
                    FilterChip(
                        selected = selectedType == OtpType.AUTHENTICATOR,
                        onClick = { selectedType = OtpType.AUTHENTICATOR },
                        label = { Text("Authenticator") },
                        modifier = Modifier.weight(1f)
                    )
                    FilterChip(
                        selected = selectedType == OtpType.EMAIL,
                        onClick = { selectedType = OtpType.EMAIL },
                        label = { Text("Email") },
                        modifier = Modifier.weight(1f)
                    )
                }

                Spacer(modifier = Modifier.height(24.dp))

                Text(
                    text = if (selectedType == OtpType.AUTHENTICATOR)
                        "Используйте приложение Google Authenticator или Authy для генерации кодов"
                    else
                        "Коды будут отправляться на вашу почту",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onBackground.copy(alpha = 0.7f),
                    textAlign = TextAlign.Center
                )

                Spacer(modifier = Modifier.weight(1f))

                Button(
                    onClick = { viewModel.enableOtp(selectedType) },
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(56.dp),
                    enabled = !state.isLoading
                ) {
                    if (state.isLoading) {
                        CircularProgressIndicator(
                            modifier = Modifier.size(24.dp),
                            color = MaterialTheme.colorScheme.onPrimary
                        )
                    } else {
                        Text("Продолжить")
                    }
                }
            } else {
                // Step 2: Confirm setup
                if (selectedType == OtpType.AUTHENTICATOR && setupInfo?.qrCodeUrl != null) {
                    Text(
                        text = "Отсканируйте QR-код в приложении Authenticator",
                        style = MaterialTheme.typography.titleMedium,
                        textAlign = TextAlign.Center
                    )

                    Spacer(modifier = Modifier.height(24.dp))

                    Image(
                        painter = rememberAsyncImagePainter(setupInfo?.qrCodeUrl),
                        contentDescription = "QR Code",
                        modifier = Modifier.size(200.dp)
                    )

                    Spacer(modifier = Modifier.height(16.dp))

                    Text(
                        text = "Или введите код вручную:",
                        style = MaterialTheme.typography.bodySmall
                    )
                    Text(
                        text = setupInfo?.secret ?: "",
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.padding(8.dp)
                    )
                } else {
                    Text(
                        text = "Введите код, отправленный на вашу почту",
                        style = MaterialTheme.typography.titleMedium,
                        textAlign = TextAlign.Center
                    )
                }

                Spacer(modifier = Modifier.height(24.dp))

                errorMessage?.let {
                    Text(
                        text = it,
                        color = MaterialTheme.colorScheme.error,
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.padding(bottom = 16.dp)
                    )
                }

                OutlinedTextField(
                    value = confirmCode,
                    onValueChange = { confirmCode = it },
                    label = { Text("Код подтверждения") },
                    modifier = Modifier.fillMaxWidth(),
                    keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(
                        keyboardType = KeyboardType.Number
                    ),
                    singleLine = true
                )

                Spacer(modifier = Modifier.height(24.dp))

                Button(
                    onClick = { viewModel.confirmOtp(selectedType, confirmCode) },
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(56.dp),
                    enabled = !state.isLoading && confirmCode.isNotBlank()
                ) {
                    if (state.isLoading) {
                        CircularProgressIndicator(
                            modifier = Modifier.size(24.dp),
                            color = MaterialTheme.colorScheme.onPrimary
                        )
                    } else {
                        Text("Подтвердить")
                    }
                }
            }
        }
    }
}
