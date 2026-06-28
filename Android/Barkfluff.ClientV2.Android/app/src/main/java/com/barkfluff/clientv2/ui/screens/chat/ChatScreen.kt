package com.barkfluff.clientv2.ui.screens.chat

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import android.content.Context
import android.net.Uri
import android.widget.Toast
import androidx.compose.foundation.layout.heightIn
import barkfluff.shared.Shared
import coil.compose.AsyncImage
import androidx.core.content.FileProvider
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.ImageCompressor
import com.barkfluff.clientv2.di.appViewModel
import com.barkfluff.clientv2.ui.components.AudioAttachment
import com.barkfluff.clientv2.ui.components.BfAvatar
import com.barkfluff.clientv2.ui.components.DocumentAttachment
import com.barkfluff.clientv2.ui.components.GifAttachment
import com.barkfluff.clientv2.ui.components.ImageItem
import com.barkfluff.clientv2.ui.components.ImageViewerOverlay
import com.barkfluff.clientv2.ui.components.VideoPlayerOverlay
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ChatScreen(chatId: String, onBack: () -> Unit) {
    val vm = appViewModel { ChatViewModel(chatId, it.chatRepository, it.realtimeService, it.globalParam) }
    val state by vm.ui.collectAsStateWithLifecycle()
    val myUserId = vm.myUserId

    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val clipboard = LocalClipboardManager.current
    val listState = rememberLazyListState()
    var input by rememberSaveable { mutableStateOf("") }
    var selectedMessage by remember { mutableStateOf<Shared.Message?>(null) }
    var editingMessage by remember { mutableStateOf<Shared.Message?>(null) }
    var replyingTo by remember { mutableStateOf<Shared.Message?>(null) }
    var forwarding by remember { mutableStateOf<Shared.Message?>(null) }
    var viewerItems by remember { mutableStateOf<List<ImageItem>?>(null) }
    var viewerStart by remember { mutableIntStateOf(0) }
    var videoToPlay by remember { mutableStateOf<String?>(null) }
    var showAttachSheet by remember { mutableStateOf(false) }
    var pendingCameraUri by remember { mutableStateOf<Uri?>(null) }

    val photoPicker = rememberLauncherForActivityResult(
        ActivityResultContracts.PickVisualMedia()
    ) { uri ->
        if (uri != null) {
            scope.launch(Dispatchers.IO) {
                val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
                if (bytes != null) vm.sendImage(bytes)
            }
        }
    }

    val videoPicker = rememberLauncherForActivityResult(
        ActivityResultContracts.PickVisualMedia()
    ) { uri ->
        if (uri != null) {
            scope.launch(Dispatchers.IO) {
                val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
                if (bytes != null) vm.sendVideo(bytes)
            }
        }
    }

    val cameraPhoto = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
        val uri = pendingCameraUri
        if (ok && uri != null) {
            scope.launch(Dispatchers.IO) {
                ImageCompressor.compressImage(uri, context).getOrNull()?.let { vm.sendImage(it) }
            }
        }
    }

    val cameraVideo = rememberLauncherForActivityResult(ActivityResultContracts.CaptureVideo()) { ok ->
        val uri = pendingCameraUri
        if (ok && uri != null) {
            scope.launch(Dispatchers.IO) {
                val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
                if (bytes != null) vm.sendVideo(bytes)
            }
        }
    }

    LaunchedEffect(state.messages.size) {
        if (state.messages.isNotEmpty()) listState.animateScrollToItem(state.messages.lastIndex)
    }

    Scaffold(
        modifier = Modifier.fillMaxSize(),
        topBar = {
            TopAppBar(
                title = {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        BfAvatar(name = state.title.ifBlank { "?" }, size = 36.dp)
                        Spacer(Modifier.width(12.dp))
                        Text(
                            text = state.title.ifBlank { "Чат" },
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis
                        )
                    }
                },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Назад")
                    }
                }
            )
        },
        bottomBar = {
            MessageInputBar(
                value = input,
                sending = state.sending,
                editing = editingMessage != null,
                replyText = replyingTo?.let { if (it.hasContent()) it.content.text else "" },
                onValueChange = { input = it },
                onAttach = { showAttachSheet = true },
                onCancelEdit = {
                    editingMessage = null
                    input = ""
                },
                onCancelReply = { replyingTo = null },
                onSend = {
                    val editing = editingMessage
                    if (editing != null) {
                        vm.editMessage(editing.id, input)
                        editingMessage = null
                    } else {
                        vm.sendText(input, replyingTo?.id ?: 0L)
                        replyingTo = null
                    }
                    input = ""
                }
            )
        }
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize().padding(padding)) {
            if (state.loading) {
                CircularProgressIndicator(modifier = Modifier.align(Alignment.Center))
            } else {
                LazyColumn(
                    state = listState,
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(12.dp)
                ) {
                    items(state.messages, key = { it.id }) { message ->
                        MessageBubble(
                            message = message,
                            isMine = message.senderId == myUserId,
                            onLongClick = { selectedMessage = message },
                            onImageClick = { imgs, idx -> viewerItems = imgs; viewerStart = idx },
                            onVideoClick = { fileId -> videoToPlay = fileId }
                        )
                    }
                }
            }
        }
    }

    selectedMessage?.let { msg ->
        val hasText = msg.hasContent() && msg.content.text.isNotBlank()
        val isMine = msg.senderId == myUserId
        ModalBottomSheet(onDismissRequest = { selectedMessage = null }) {
            Column(modifier = Modifier.fillMaxWidth().padding(bottom = 24.dp)) {
                ActionRow("Ответить") {
                    replyingTo = msg
                    editingMessage = null
                    selectedMessage = null
                }
                ActionRow("Переслать") {
                    forwarding = msg
                    selectedMessage = null
                }
                if (hasText) {
                    ActionRow("Копировать") {
                        clipboard.setText(AnnotatedString(msg.content.text))
                        selectedMessage = null
                    }
                }
                if (isMine && hasText) {
                    ActionRow("Редактировать") {
                        editingMessage = msg
                        replyingTo = null
                        input = msg.content.text
                        selectedMessage = null
                    }
                }
                if (isMine) {
                    ActionRow("Удалить", destructive = true) {
                        vm.deleteMessage(msg.id)
                        selectedMessage = null
                    }
                }
            }
        }
    }

    forwarding?.let { msg ->
        val pickerVm = appViewModel { ForwardChatsViewModel(it.grpcManager) }
        val pickerState by pickerVm.ui.collectAsStateWithLifecycle()
        ModalBottomSheet(onDismissRequest = { forwarding = null }) {
            Column(modifier = Modifier.fillMaxWidth().padding(bottom = 24.dp)) {
                Text(
                    text = "Переслать в…",
                    style = MaterialTheme.typography.titleMedium,
                    modifier = Modifier.padding(horizontal = 24.dp, vertical = 12.dp)
                )
                if (pickerState.loading) {
                    Box(Modifier.fillMaxWidth().padding(24.dp), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator()
                    }
                } else {
                    LazyColumn(modifier = Modifier.fillMaxWidth().heightIn(max = 400.dp)) {
                        items(pickerState.chats, key = { it.id }) { chat ->
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .clickable {
                                        vm.forwardMessage(msg.id, chat.id)
                                        Toast.makeText(context, "Переслано", Toast.LENGTH_SHORT).show()
                                        forwarding = null
                                    }
                                    .padding(horizontal = 16.dp, vertical = 10.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                BfAvatar(name = chat.title.ifBlank { "?" }, size = 40.dp)
                                Spacer(Modifier.width(12.dp))
                                Text(
                                    text = chat.title.ifBlank { "Без названия" },
                                    style = MaterialTheme.typography.bodyLarge,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis
                                )
                            }
                        }
                    }
                }
            }
        }
    }

    viewerItems?.let { items ->
        ImageViewerOverlay(items = items, startIndex = viewerStart) { viewerItems = null }
    }

    videoToPlay?.let { fileId ->
        VideoPlayerOverlay(fileId = fileId) { videoToPlay = null }
    }

    if (showAttachSheet) {
        ModalBottomSheet(onDismissRequest = { showAttachSheet = false }) {
            Column(modifier = Modifier.fillMaxWidth().padding(bottom = 24.dp)) {
                ActionRow("Фото из галереи") {
                    showAttachSheet = false
                    photoPicker.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
                }
                ActionRow("Видео из галереи") {
                    showAttachSheet = false
                    videoPicker.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.VideoOnly))
                }
                ActionRow("Снять фото") {
                    showAttachSheet = false
                    val uri = createMediaUri(context, "jpg")
                    pendingCameraUri = uri
                    cameraPhoto.launch(uri)
                }
                ActionRow("Снять видео") {
                    showAttachSheet = false
                    val uri = createMediaUri(context, "mp4")
                    pendingCameraUri = uri
                    cameraVideo.launch(uri)
                }
            }
        }
    }
}

/** Временный файл в cacheDir/media_files/ + FileProvider-URI для записи с камеры. */
private fun createMediaUri(context: Context, extension: String): Uri {
    val dir = java.io.File(context.cacheDir, "media_files").apply { mkdirs() }
    val file = java.io.File(dir, "cam_${System.currentTimeMillis()}.$extension")
    return FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
}

@Composable
private fun ActionRow(label: String, destructive: Boolean = false, onClick: () -> Unit) {
    Text(
        text = label,
        style = MaterialTheme.typography.bodyLarge,
        color = if (destructive) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurface,
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 24.dp, vertical = 16.dp)
    )
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun MessageBubble(
    message: Shared.Message,
    isMine: Boolean,
    onLongClick: () -> Unit,
    onImageClick: (List<ImageItem>, Int) -> Unit,
    onVideoClick: (String) -> Unit
) {
    val scheme = MaterialTheme.colorScheme
    val bubbleColor = if (isMine) scheme.primary else scheme.surfaceContainerHigh
    val textColor = if (isMine) scheme.onPrimary else scheme.onSurface
    val bubbleShape = RoundedCornerShape(
        topStart = 20.dp,
        topEnd = 20.dp,
        bottomStart = if (isMine) 20.dp else 4.dp,
        bottomEnd = if (isMine) 4.dp else 20.dp
    )

    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
        horizontalArrangement = if (isMine) Arrangement.End else Arrangement.Start
    ) {
        Surface(
            color = bubbleColor,
            shape = bubbleShape,
            modifier = Modifier
                .widthIn(max = 300.dp)
                .combinedClickable(onClick = {}, onLongClick = onLongClick)
        ) {
            Column(modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp)) {
                val attachments = if (message.hasContent()) message.content.attachmentsList else emptyList()

                val forwarded = attachments
                    .firstOrNull { it.type == Shared.MessageAttachmentType.FORWARDED_MESSAGE }
                    ?.forwardedMessage
                if (forwarded != null) {
                    QuoteBlock(author = forwarded.authorName, text = forwarded.text, mine = isMine)
                }

                val images = attachments.filter {
                    it.type == Shared.MessageAttachmentType.IMAGE && it.previewUrl.isNotBlank()
                }
                val imageItems = images.map { ImageItem(it.fileId, it.previewUrl) }
                images.forEachIndexed { idx, att ->
                    AsyncImage(
                        model = att.previewUrl,
                        contentDescription = null,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier
                            .padding(bottom = 4.dp)
                            .size(220.dp)
                            .clip(RoundedCornerShape(12.dp))
                            .clickable { onImageClick(imageItems, idx) }
                    )
                }

                attachments.filter { it.type == Shared.MessageAttachmentType.VIDEO }.forEach { att ->
                    Box(
                        modifier = Modifier
                            .padding(bottom = 4.dp)
                            .size(220.dp)
                            .clip(RoundedCornerShape(12.dp))
                            .background(MaterialTheme.colorScheme.surfaceContainerHighest)
                            .clickable { onVideoClick(att.fileId) },
                        contentAlignment = Alignment.Center
                    ) {
                        if (att.previewUrl.isNotBlank()) {
                            AsyncImage(
                                model = att.previewUrl,
                                contentDescription = null,
                                contentScale = ContentScale.Crop,
                                modifier = Modifier.fillMaxSize()
                            )
                        }
                        Icon(
                            imageVector = Icons.Filled.PlayArrow,
                            contentDescription = "Воспроизвести",
                            tint = Color.White,
                            modifier = Modifier.size(56.dp)
                        )
                    }
                }
                attachments.filter {
                    it.type == Shared.MessageAttachmentType.AUDIO ||
                        it.type == Shared.MessageAttachmentType.VOICE
                }.forEach { att ->
                    AudioAttachment(fileId = att.fileId, fileName = att.fileName, mine = isMine)
                }

                attachments.filter { it.type == Shared.MessageAttachmentType.DOCUMENT }.forEach { att ->
                    DocumentAttachment(
                        fileId = att.fileId,
                        fileName = att.fileName,
                        sizeBytes = att.attachmentSize,
                        mine = isMine
                    )
                }

                attachments.filter { it.type == Shared.MessageAttachmentType.GIF }.forEach { att ->
                    GifAttachment(fileId = att.fileId, previewUrl = att.previewUrl)
                }

                val text = if (message.hasContent()) message.content.text else ""
                if (text.isNotBlank()) {
                    Text(text = text, color = textColor, style = MaterialTheme.typography.bodyLarge)
                }
                Text(
                    text = formatTime(message) + if (message.isEdited) " · ред." else "",
                    color = textColor.copy(alpha = 0.7f),
                    style = MaterialTheme.typography.labelSmall,
                    textAlign = TextAlign.End,
                    modifier = Modifier.fillMaxWidth()
                )
            }
        }
    }
}

@Composable
private fun QuoteBlock(author: String, text: String, mine: Boolean) {
    val accent = if (mine) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.primary
    val container = if (mine) MaterialTheme.colorScheme.onPrimary.copy(alpha = 0.12f)
    else MaterialTheme.colorScheme.surfaceContainerHighest
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(bottom = 4.dp)
            .background(container, RoundedCornerShape(8.dp))
            .padding(horizontal = 8.dp, vertical = 4.dp)
    ) {
        if (author.isNotBlank()) {
            Text(
                text = author,
                style = MaterialTheme.typography.labelMedium,
                color = accent,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
        }
        if (text.isNotBlank()) {
            Text(
                text = text,
                style = MaterialTheme.typography.bodySmall,
                color = if (mine) MaterialTheme.colorScheme.onPrimary.copy(alpha = 0.85f)
                else MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis
            )
        }
    }
}

@Composable
private fun MessageInputBar(
    value: String,
    sending: Boolean,
    editing: Boolean,
    replyText: String?,
    onValueChange: (String) -> Unit,
    onAttach: () -> Unit,
    onCancelEdit: () -> Unit,
    onCancelReply: () -> Unit,
    onSend: () -> Unit
) {
    Surface(color = MaterialTheme.colorScheme.surfaceContainer) {
        Column {
            if (editing) {
                ContextRow(label = "Редактирование сообщения", onCancel = onCancelEdit)
            } else if (replyText != null) {
                ContextRow(
                    label = "Ответ: " + replyText.ifBlank { "вложение" },
                    onCancel = onCancelReply
                )
            }
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                IconButton(onClick = onAttach, enabled = !sending && !editing) {
                    Icon(Icons.Filled.Add, contentDescription = "Вложение")
                }
                TextField(
                    value = value,
                    onValueChange = onValueChange,
                    modifier = Modifier.weight(1f),
                    placeholder = { Text("Сообщение") },
                    maxLines = 4,
                    shape = RoundedCornerShape(24.dp),
                    colors = TextFieldDefaults.colors(
                        focusedIndicatorColor = Color.Transparent,
                        unfocusedIndicatorColor = Color.Transparent
                    )
                )
                Spacer(Modifier.width(4.dp))
                FilledIconButton(onClick = onSend, enabled = !sending && value.isNotBlank()) {
                    if (sending) {
                        CircularProgressIndicator(
                            modifier = Modifier.size(20.dp),
                            color = MaterialTheme.colorScheme.onPrimary
                        )
                    } else {
                        Icon(Icons.AutoMirrored.Filled.Send, contentDescription = "Отправить")
                    }
                }
            }
        }
    }
}

@Composable
private fun ContextRow(label: String, onCancel: () -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(start = 16.dp, top = 8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.primary,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f)
        )
        IconButton(onClick = onCancel) {
            Icon(Icons.Filled.Close, contentDescription = "Отменить")
        }
    }
}

private val bubbleTimeFormat = SimpleDateFormat("HH:mm", Locale.getDefault())

private fun formatTime(message: Shared.Message): String {
    if (!message.hasSentAt()) return ""
    val millis = message.sentAt.seconds * 1000 + message.sentAt.nanos / 1_000_000
    return bubbleTimeFormat.format(Date(millis))
}
