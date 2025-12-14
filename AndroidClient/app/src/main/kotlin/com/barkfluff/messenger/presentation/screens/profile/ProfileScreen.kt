package com.barkfluff.messenger.presentation.screens.profile

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Message
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.barkfluff.messenger.R
import com.barkfluff.messenger.presentation.viewmodel.UserViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ProfileScreen(
    navController: NavController,
    userId: Long,
    viewModel: UserViewModel = hiltViewModel()
) {
    val user by viewModel.currentUser.collectAsState()

    LaunchedEffect(userId) {
        viewModel.loadUser(userId)
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(stringResource(R.string.profile)) },
                navigationIcon = {
                    IconButton(onClick = { navController.navigateUp() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Back")
                    }
                }
            )
        },
        floatingActionButton = {
            FloatingActionButton(onClick = { /* TODO: Start chat */ }) {
                Icon(Icons.Default.Message, contentDescription = "Message")
            }
        }
    ) { padding ->
        user?.let { currentUser ->
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding)
                    .padding(24.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                // Avatar
                if (currentUser.profilePictureUrl != null) {
                    AsyncImage(
                        model = currentUser.profilePictureUrl,
                        contentDescription = currentUser.fullName,
                        modifier = Modifier
                            .size(120.dp)
                            .clip(CircleShape)
                    )
                } else {
                    Surface(
                        modifier = Modifier.size(120.dp),
                        shape = CircleShape,
                        color = MaterialTheme.colorScheme.primaryContainer
                    ) {
                        Box(contentAlignment = Alignment.Center) {
                            Text(
                                text = currentUser.firstName.take(1).uppercase(),
                                style = MaterialTheme.typography.displayMedium,
                                color = MaterialTheme.colorScheme.onPrimaryContainer
                            )
                        }
                    }
                }

                Spacer(modifier = Modifier.height(24.dp))

                Text(
                    text = currentUser.fullName,
                    style = MaterialTheme.typography.headlineMedium,
                    fontWeight = FontWeight.Bold
                )

                Spacer(modifier = Modifier.height(8.dp))

                Text(
                    text = "@${currentUser.username}",
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.onBackground.copy(alpha = 0.6f)
                )

                currentUser.bio?.let { bio ->
                    Spacer(modifier = Modifier.height(16.dp))
                    Text(
                        text = bio,
                        style = MaterialTheme.typography.bodyLarge,
                        color = MaterialTheme.colorScheme.onBackground.copy(alpha = 0.8f)
                    )
                }

                // Badges
                if (currentUser.badges.isNotEmpty()) {
                    Spacer(modifier = Modifier.height(24.dp))
                    Text(
                        text = "Значки",
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.Bold
                    )
                    // TODO: Display badges
                }
            }
        }
    }
}
