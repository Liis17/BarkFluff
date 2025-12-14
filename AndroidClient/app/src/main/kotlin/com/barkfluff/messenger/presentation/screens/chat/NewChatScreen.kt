package com.barkfluff.messenger.presentation.screens.chat

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.barkfluff.messenger.R
import com.barkfluff.messenger.domain.model.User
import com.barkfluff.messenger.presentation.navigation.Screen
import com.barkfluff.messenger.presentation.viewmodel.UserViewModel
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NewChatScreen(
    navController: NavController,
    userViewModel: UserViewModel = hiltViewModel(),
    chatViewModel: com.barkfluff.messenger.presentation.viewmodel.ChatViewModel = hiltViewModel()
) {
    var searchQuery by remember { mutableStateOf("") }
    val searchResults by userViewModel.searchResults.collectAsState()
    val coroutineScope = rememberCoroutineScope()
    var searchJob by remember { mutableStateOf<Job?>(null) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(stringResource(R.string.new_chat)) },
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
        ) {
            // Search bar
            OutlinedTextField(
                value = searchQuery,
                onValueChange = { query ->
                    searchQuery = query
                    searchJob?.cancel()
                    searchJob = coroutineScope.launch {
                        delay(500) // Debounce
                        if (query.isNotBlank()) {
                            userViewModel.searchUsers(query)
                        }
                    }
                },
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(16.dp),
                placeholder = { Text(stringResource(R.string.search)) },
                leadingIcon = {
                    Icon(Icons.Default.Search, contentDescription = null)
                },
                singleLine = true
            )

            // Results
            LazyColumn(modifier = Modifier.fillMaxSize()) {
                items(searchResults, key = { it.id }) { user ->
                    UserListItem(
                        user = user,
                        onClick = {
                            // Start chat with user
                            chatViewModel.sendMessage(recipientId = user.id, text = "")
                            navController.navigateUp()
                        }
                    )
                    Divider()
                }
            }
        }
    }
}

@Composable
fun UserListItem(
    user: User,
    onClick: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(16.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        if (user.profilePictureUrl != null) {
            AsyncImage(
                model = user.profilePictureUrl,
                contentDescription = user.fullName,
                modifier = Modifier
                    .size(48.dp)
                    .clip(CircleShape)
            )
        } else {
            Surface(
                modifier = Modifier.size(48.dp),
                shape = CircleShape,
                color = MaterialTheme.colorScheme.primaryContainer
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Text(
                        text = user.firstName.take(1).uppercase(),
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.onPrimaryContainer
                    )
                }
            }
        }

        Spacer(modifier = Modifier.width(16.dp))

        Column {
            Text(
                text = user.fullName,
                style = MaterialTheme.typography.titleMedium
            )
            Text(
                text = "@${user.username}",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onBackground.copy(alpha = 0.6f)
            )
        }
    }
}
