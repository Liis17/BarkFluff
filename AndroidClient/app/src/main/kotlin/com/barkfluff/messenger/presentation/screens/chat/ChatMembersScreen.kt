package com.barkfluff.messenger.presentation.screens.chat

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Delete
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
import com.barkfluff.messenger.data.local.SessionManager
import com.barkfluff.messenger.data.repository.ChatRepository
import com.barkfluff.messenger.data.repository.UserRepository
import com.barkfluff.messenger.domain.model.ChatMember
import com.barkfluff.messenger.domain.model.User
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ChatMembersScreen(
    navController: NavController,
    chatId: String,
    chatRepository: ChatRepository = hiltViewModel(),
    userRepository: UserRepository = hiltViewModel(),
    sessionManager: SessionManager = hiltViewModel()
) {
    var members by remember { mutableStateOf<List<ChatMember>>(emptyList()) }
    var membersWithUsers by remember { mutableStateOf<List<Pair<ChatMember, User?>>>(emptyList()) }
    var isLoading by remember { mutableStateOf(true) }
    var currentUserId by remember { mutableStateOf<Long?>(null) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(chatId) {
        currentUserId = sessionManager.userId.first()

        chatRepository.getChatMembers(chatId)
            .onSuccess { membersList ->
                members = membersList
                // Load user info for each member
                val pairs = membersList.map { member ->
                    val user = userRepository.getUser(member.userId).getOrNull()
                    member to user
                }
                membersWithUsers = pairs
            }
        isLoading = false
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Участники (${members.size})") },
                navigationIcon = {
                    IconButton(onClick = { navController.navigateUp() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Back")
                    }
                }
            )
        }
    ) { padding ->
        if (isLoading) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentAlignment = Alignment.Center
            ) {
                CircularProgressIndicator()
            }
        } else {
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding)
            ) {
                items(membersWithUsers, key = { it.first.userId }) { (member, user) ->
                    ChatMemberItem(
                        member = member,
                        user = user,
                        canKick = currentUserId != member.userId,
                        onKick = {
                            scope.launch {
                                chatRepository.kickUser(chatId, member.userId)
                                    .onSuccess {
                                        members = members.filter { it.userId != member.userId }
                                        membersWithUsers = membersWithUsers.filter { it.first.userId != member.userId }
                                    }
                            }
                        }
                    )
                    Divider()
                }
            }
        }
    }
}

@Composable
fun ChatMemberItem(
    member: ChatMember,
    user: User?,
    canKick: Boolean,
    onKick: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(enabled = user != null) { /* Navigate to profile */ }
            .padding(16.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        if (user?.profilePictureUrl != null) {
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
                        text = user?.firstName?.take(1)?.uppercase() ?: "?",
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.onPrimaryContainer
                    )
                }
            }
        }

        Spacer(modifier = Modifier.width(16.dp))

        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = user?.fullName ?: "Загрузка...",
                style = MaterialTheme.typography.titleMedium
            )
            user?.let {
                Text(
                    text = "@${it.username}",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onBackground.copy(alpha = 0.6f)
                )
            }
        }

        if (canKick) {
            IconButton(onClick = onKick) {
                Icon(
                    Icons.Default.Delete,
                    contentDescription = "Kick",
                    tint = MaterialTheme.colorScheme.error
                )
            }
        }
    }
}
