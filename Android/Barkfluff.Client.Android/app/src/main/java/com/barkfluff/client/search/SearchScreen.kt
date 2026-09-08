package com.barkfluff.client.search

import android.app.Activity
import android.os.Build
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DockedSearchBar
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SearchBarDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.Typography
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.compositeOver
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.colorResource
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.view.WindowCompat
import com.barkfluff.client.R
import com.barkfluff.client.view.AvatarView

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SearchScreen(
    isPrivateMode: Boolean,
    uiState: SearchUiState,
    isActionInProgress: Boolean,
    onQueryChanged: (String) -> Unit,
    onSubmit: () -> Unit,
    onRetry: () -> Unit,
    onClear: () -> Unit,
    onBack: () -> Unit,
    onUserClick: (SearchUser) -> Unit,
    getAvatarUrl: suspend (String) -> String?
) {
    val focusRequester = remember { FocusRequester() }
    val keyboardController = LocalSoftwareKeyboardController.current

    LaunchedEffect(focusRequester) {
        withFrameNanos { }
        focusRequester.requestFocus()
        keyboardController?.show()
    }

    Surface(
        modifier = Modifier
            .fillMaxSize()
            .imePadding(),
        color = MaterialTheme.colorScheme.background
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            SearchHeader(
                query = uiState.query,
                placeholder = if (isPrivateMode) {
                    stringResourceCompat(R.string.search_private_hint)
                } else {
                    stringResourceCompat(R.string.search_users_hint)
                },
                focusRequester = focusRequester,
                onQueryChanged = onQueryChanged,
                onSubmit = onSubmit,
                onClear = onClear,
                onBack = onBack
            )

            Box(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxWidth()
                    .navigationBarsPadding()
            ) {
                SearchPhaseContent(
                    isPrivateMode = isPrivateMode,
                    uiState = uiState,
                    isActionInProgress = isActionInProgress,
                    onRetry = onRetry,
                    onUserClick = onUserClick,
                    getAvatarUrl = getAvatarUrl
                )
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SearchHeader(
    query: String,
    placeholder: String,
    focusRequester: FocusRequester,
    onQueryChanged: (String) -> Unit,
    onSubmit: () -> Unit,
    onClear: () -> Unit,
    onBack: () -> Unit
) {
    var isFocused by remember { mutableStateOf(false) }
    val backDescription = stringResourceCompat(R.string.cd_back)
    val clearDescription = stringResourceCompat(R.string.cd_clear_search)
    val baseContainerColor = MaterialTheme.colorScheme.surfaceContainerHigh
    val containerColor = if (isFocused) {
        MaterialTheme.colorScheme.primary
            .copy(alpha = 0.08f)
            .compositeOver(baseContainerColor)
    } else {
        baseContainerColor
    }

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .statusBarsPadding()
            .padding(start = 4.dp, end = 16.dp, top = 8.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        IconButton(
            onClick = onBack,
            modifier = Modifier
                .size(48.dp)
                .semantics {
                    contentDescription = backDescription
                    role = Role.Button
                }
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_arrow_back),
                contentDescription = null,
                modifier = Modifier.size(24.dp)
            )
        }

        DockedSearchBar(
            query = query,
            onQueryChange = onQueryChanged,
            onSearch = {
                onSubmit()
            },
            // This screen owns the results below the bar, so the M3 expansion surface is kept
            // collapsed. The input still receives focus and IME actions normally.
            active = false,
            onActiveChange = {},
            modifier = Modifier
                .weight(1f)
                .heightIn(min = 56.dp)
                .focusRequester(focusRequester)
                .onFocusChanged { isFocused = it.isFocused },
            enabled = true,
            placeholder = { Text(text = placeholder) },
            leadingIcon = {
                Icon(
                    painter = painterResource(R.drawable.ic_search),
                    contentDescription = null,
                    modifier = Modifier.size(24.dp)
                )
            },
            trailingIcon = if (query.isNotEmpty()) {
                {
                    IconButton(
                        onClick = onClear,
                        modifier = Modifier
                            .size(48.dp)
                            .semantics {
                                contentDescription = clearDescription
                                role = Role.Button
                            }
                    ) {
                        Icon(
                            painter = painterResource(R.drawable.ic_close),
                            contentDescription = null,
                            modifier = Modifier.size(20.dp)
                        )
                    }
                }
            } else {
                null
            },
            shape = RoundedCornerShape(28.dp),
            colors = SearchBarDefaults.colors(
                containerColor = containerColor,
                dividerColor = Color.Transparent
            ),
            tonalElevation = 0.dp,
            shadowElevation = 0.dp,
            content = {}
        )
    }
}

@Composable
private fun SearchPhaseContent(
    isPrivateMode: Boolean,
    uiState: SearchUiState,
    isActionInProgress: Boolean,
    onRetry: () -> Unit,
    onUserClick: (SearchUser) -> Unit,
    getAvatarUrl: suspend (String) -> String?
) {
    when (uiState.phase) {
        SearchPhase.Idle -> SearchMessageState(
            icon = R.drawable.ic_search,
            title = if (isPrivateMode) {
                stringResourceCompat(R.string.search_private_title)
            } else {
                stringResourceCompat(R.string.search_users_prompt_title)
            },
            description = if (isPrivateMode) {
                stringResourceCompat(R.string.search_private_description)
            } else {
                stringResourceCompat(R.string.search_users_hint_long)
            }
        )

        SearchPhase.TooShort -> SearchMessageState(
            icon = R.drawable.ic_search,
            title = stringResourceCompat(R.string.search_users_title),
            description = stringResourceCompat(R.string.search_too_short)
        )

        SearchPhase.Loading -> LoadingState()

        SearchPhase.Results -> SearchResults(
            users = uiState.users,
            isActionInProgress = isActionInProgress,
            onUserClick = onUserClick,
            getAvatarUrl = getAvatarUrl
        )

        SearchPhase.Empty -> SearchMessageState(
            icon = R.drawable.ic_search,
            title = stringResourceCompat(R.string.search_nothing_found),
            description = stringResourceCompat(R.string.search_try_different)
        )

        SearchPhase.Error -> SearchErrorState(onRetry = onRetry)
    }
}

@Composable
private fun SearchResults(
    users: List<SearchUser>,
    isActionInProgress: Boolean,
    onUserClick: (SearchUser) -> Unit,
    getAvatarUrl: suspend (String) -> String?
) {
    LazyColumn(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        items(
            items = users,
            key = { it.userData.userId }
        ) { user ->
            SearchResultItem(
                user = user,
                isActionInProgress = isActionInProgress,
                onClick = { onUserClick(user) },
                getAvatarUrl = getAvatarUrl,
                modifier = Modifier
                    .fillMaxWidth()
                    .widthIn(max = 720.dp)
            )
        }
    }
}

@Composable
private fun SearchResultItem(
    user: SearchUser,
    isActionInProgress: Boolean,
    onClick: () -> Unit,
    getAvatarUrl: suspend (String) -> String?,
    modifier: Modifier = Modifier
) {
    val shape = RoundedCornerShape(20.dp)
    val accessibilityText = listOf(user.displayFullName, user.displayUsername)
        .filter { it.isNotBlank() }
        .joinToString(", ")

    Surface(
        modifier = modifier
            .heightIn(min = 72.dp)
            .clip(shape = shape)
            .clickable(
                enabled = !isActionInProgress,
                role = Role.Button,
                onClick = onClick
            )
            .semantics {
                contentDescription = accessibilityText
                role = Role.Button
            },
        shape = shape,
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        tonalElevation = 0.dp,
        shadowElevation = 0.dp
    ) {
        ListItem(
            modifier = Modifier.fillMaxWidth(),
            headlineContent = {
                Text(
                    text = user.displayFullName,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
            },
            supportingContent = {
                Text(
                    text = user.displayUsername.ifBlank { " " },
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
            },
            leadingContent = {
                SearchAvatar(
                    fileId = user.displayAvatarFileId,
                    displayName = user.displayFullName,
                    userId = user.userData.userId,
                    getAvatarUrl = getAvatarUrl
                )
            },
            colors = ListItemDefaults.colors(containerColor = Color.Transparent),
            tonalElevation = 0.dp,
            shadowElevation = 0.dp
        )
    }
}

@Composable
private fun SearchAvatar(
    fileId: String?,
    displayName: String,
    userId: Long,
    getAvatarUrl: suspend (String) -> String?
) {
    AndroidView(
        factory = { context -> AvatarView(context) },
        modifier = Modifier
            .size(48.dp)
            .clip(CircleShape)
            .semantics { contentDescription = displayName },
        update = { avatarView ->
            avatarView.loadAvatarByFileId(
                fileId = fileId,
                displayName = displayName,
                userId = userId,
                size = 96,
                getUrlCallback = {
                    if (fileId == null) null else getAvatarUrl(fileId)
                }
            )
        }
    )
}

@Composable
private fun SearchMessageState(
    icon: Int,
    title: String,
    description: String
) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        contentAlignment = Alignment.Center
    ) {
        Column(
            modifier = Modifier.widthIn(max = 420.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            Surface(
                modifier = Modifier.size(80.dp),
                shape = RoundedCornerShape(28.dp),
                color = MaterialTheme.colorScheme.primaryContainer
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(
                        painter = painterResource(icon),
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.onPrimaryContainer,
                        modifier = Modifier.size(32.dp)
                    )
                }
            }
            Text(
                text = title,
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.SemiBold,
                textAlign = TextAlign.Center
            )
            Text(
                text = description,
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center
            )
        }
    }
}

@Composable
private fun LoadingState() {
    val loadingDescription = stringResourceCompat(R.string.search_loading)
    Box(
        modifier = Modifier.fillMaxSize(),
        contentAlignment = Alignment.Center
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            CircularProgressIndicator(
                modifier = Modifier
                    .size(40.dp)
                    .semantics {
                        contentDescription = loadingDescription
                    },
                color = MaterialTheme.colorScheme.primary,
                strokeWidth = 4.dp
            )
            Text(
                text = stringResourceCompat(R.string.search_loading),
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodyLarge
            )
        }
    }
}

@Composable
private fun SearchErrorState(onRetry: () -> Unit) {
    val title = stringResourceCompat(R.string.search_error_title)
    val description = stringResourceCompat(R.string.search_error_description)
    val retryLabel = stringResourceCompat(R.string.search_retry)
    Box(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        contentAlignment = Alignment.Center
    ) {
        Column(
            modifier = Modifier.widthIn(max = 420.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            Surface(
                modifier = Modifier.size(80.dp),
                shape = RoundedCornerShape(28.dp),
                color = MaterialTheme.colorScheme.errorContainer
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(
                        painter = painterResource(R.drawable.ic_info),
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.onErrorContainer,
                        modifier = Modifier.size(32.dp)
                    )
                }
            }
            Text(
                text = title,
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.SemiBold,
                textAlign = TextAlign.Center
            )
            Text(
                text = description,
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center
            )
            Button(
                onClick = onRetry,
                modifier = Modifier
                    .heightIn(min = 48.dp)
                    .semantics {
                        contentDescription = retryLabel
                        role = Role.Button
                    },
                shape = RoundedCornerShape(20.dp),
                colors = ButtonDefaults.buttonColors()
            ) {
                Text(text = retryLabel)
            }
        }
    }
}

@Composable
fun BarkFluffSearchTheme(content: @Composable () -> Unit) {
    val context = LocalContext.current
    val darkTheme = androidx.compose.foundation.isSystemInDarkTheme()
    val colorScheme = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
        if (darkTheme) {
            androidx.compose.material3.dynamicDarkColorScheme(context)
        } else {
            androidx.compose.material3.dynamicLightColorScheme(context)
        }
    } else {
        fallbackColorScheme(darkTheme)
    }
    val baseTypography = Typography()
    val typography = baseTypography.copy(
        headlineSmall = baseTypography.headlineSmall.copy(fontWeight = FontWeight.SemiBold)
    )

    val view = androidx.compose.ui.platform.LocalView.current
    SideEffect {
        val window = (view.context as? Activity)?.window ?: return@SideEffect
        val controller = WindowCompat.getInsetsController(window, view)
        controller.isAppearanceLightStatusBars = !darkTheme
        controller.isAppearanceLightNavigationBars = !darkTheme
    }

    androidx.compose.material3.MaterialTheme(
        colorScheme = colorScheme,
        typography = typography,
        content = content
    )
}

@Composable
private fun fallbackColorScheme(darkTheme: Boolean) = if (darkTheme) {
    darkColorScheme(
        primary = colorResource(R.color.primary),
        onPrimary = colorResource(R.color.on_primary),
        primaryContainer = colorResource(R.color.primary_container),
        onPrimaryContainer = colorResource(R.color.on_primary_container),
        secondary = colorResource(R.color.secondary),
        onSecondary = colorResource(R.color.on_secondary),
        secondaryContainer = colorResource(R.color.secondary_container),
        onSecondaryContainer = colorResource(R.color.on_secondary_container),
        error = colorResource(R.color.error),
        onError = colorResource(R.color.on_error),
        errorContainer = colorResource(R.color.error_container),
        onErrorContainer = colorResource(R.color.on_error_container),
        background = colorResource(R.color.background),
        onBackground = colorResource(R.color.on_background),
        surface = colorResource(R.color.surface),
        onSurface = colorResource(R.color.on_surface),
        surfaceVariant = colorResource(R.color.surface_container),
        onSurfaceVariant = colorResource(R.color.on_surface_variant),
        outline = colorResource(R.color.outline),
        outlineVariant = colorResource(R.color.outline_variant),
        surfaceContainerLow = colorResource(R.color.surface_container_low),
        surfaceContainerHigh = colorResource(R.color.surface_container_high)
    )
} else {
    lightColorScheme(
        primary = colorResource(R.color.primary),
        onPrimary = colorResource(R.color.on_primary),
        primaryContainer = colorResource(R.color.primary_container),
        onPrimaryContainer = colorResource(R.color.on_primary_container),
        secondary = colorResource(R.color.secondary),
        onSecondary = colorResource(R.color.on_secondary),
        secondaryContainer = colorResource(R.color.secondary_container),
        onSecondaryContainer = colorResource(R.color.on_secondary_container),
        error = colorResource(R.color.error),
        onError = colorResource(R.color.on_error),
        errorContainer = colorResource(R.color.error_container),
        onErrorContainer = colorResource(R.color.on_error_container),
        background = colorResource(R.color.background),
        onBackground = colorResource(R.color.on_background),
        surface = colorResource(R.color.surface),
        onSurface = colorResource(R.color.on_surface),
        surfaceVariant = colorResource(R.color.surface_container),
        onSurfaceVariant = colorResource(R.color.on_surface_variant),
        outline = colorResource(R.color.outline),
        outlineVariant = colorResource(R.color.outline_variant),
        surfaceContainerLow = colorResource(R.color.surface_container_low),
        surfaceContainerHigh = colorResource(R.color.surface_container_high)
    )
}

/** Keeps Compose UI files free of Android View context plumbing in previews and tests. */
@Composable
private fun stringResourceCompat(id: Int): String =
    androidx.compose.ui.res.stringResource(id)
