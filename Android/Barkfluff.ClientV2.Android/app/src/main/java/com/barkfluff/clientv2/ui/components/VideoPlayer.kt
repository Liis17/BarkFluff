package com.barkfluff.clientv2.ui.components

import androidx.annotation.OptIn
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.media3.common.MediaItem
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.ui.PlayerView
import com.barkfluff.clientv2.di.LocalAppContainer

/**
 * Полноэкранный видео-плеер (ExoPlayer/media3). URL файла резолвится через getFileDownloadUrl,
 * проигрывается через [PlayerView] во [AndroidView]. Плеер освобождается при закрытии.
 */
@OptIn(UnstableApi::class)
@Composable
fun VideoPlayerOverlay(fileId: String, onDismiss: () -> Unit) {
    val container = LocalAppContainer.current
    val context = LocalContext.current
    var url by remember(fileId) { mutableStateOf<String?>(null) }

    LaunchedEffect(fileId) {
        if (fileId.isNotBlank()) url = container.grpcManager.getFileDownloadUrl(fileId).getOrNull()
    }

    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false)
    ) {
        Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
            val resolved = url
            if (resolved == null) {
                CircularProgressIndicator(modifier = Modifier.align(Alignment.Center), color = Color.White)
            } else {
                val exoPlayer = remember(resolved) {
                    ExoPlayer.Builder(context).build().apply {
                        setMediaItem(MediaItem.fromUri(resolved))
                        prepare()
                        playWhenReady = true
                    }
                }
                DisposableEffect(resolved) {
                    onDispose { exoPlayer.release() }
                }
                AndroidView(
                    factory = { ctx -> PlayerView(ctx).apply { player = exoPlayer } },
                    modifier = Modifier.fillMaxSize()
                )
            }
            IconButton(
                onClick = onDismiss,
                modifier = Modifier.align(Alignment.TopStart).padding(8.dp)
            ) {
                Icon(Icons.Filled.Close, contentDescription = "Закрыть", tint = Color.White)
            }
        }
    }
}
