package com.barkfluff.clientv2.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.barkfluff.client.utils.AudioCallbacks
import com.barkfluff.client.utils.AudioPlayerHelper
import com.barkfluff.clientv2.di.LocalAppContainer
import kotlinx.coroutines.launch

/**
 * Вложение-аудио (AUDIO/VOICE): круглая кнопка play/pause + перематываемый слайдер + таймер.
 * Файл скачивается через ChatRepository (кэшируется в FileCache), проигрывается синглтоном
 * [AudioPlayerHelper] — одновременно играет только один файл во всём приложении.
 */
@Composable
fun AudioAttachment(fileId: String, fileName: String, mine: Boolean) {
    val container = LocalAppContainer.current
    val scope = rememberCoroutineScope()

    var isPlaying by remember(fileId) { mutableStateOf(false) }
    var positionMs by remember(fileId) { mutableIntStateOf(0) }
    var durationMs by remember(fileId) { mutableIntStateOf(0) }
    var loading by remember(fileId) { mutableStateOf(false) }

    val callbacks = remember(fileId) {
        object : AudioCallbacks {
            override fun onProgress(positionMs2: Int, durationMs2: Int) {
                positionMs = positionMs2
                if (durationMs2 > 0) durationMs = durationMs2
            }
            override fun onStateChanged(playing: Boolean) { isPlaying = playing }
            override fun onError() { isPlaying = false; loading = false }
            override fun onComplete() { isPlaying = false; positionMs = 0 }
        }
    }

    // Перепривязка к синглтону, если этот файл уже активен (пересоздание/возврат на экран).
    DisposableEffect(fileId) {
        if (AudioPlayerHelper.isActiveFile(fileId)) {
            AudioPlayerHelper.swapCallbacks(fileId, callbacks)
            isPlaying = AudioPlayerHelper.isPlaying()
            durationMs = AudioPlayerHelper.getDuration()
            positionMs = AudioPlayerHelper.getCurrentPosition()
        }
        onDispose { AudioPlayerHelper.swapCallbacks(fileId, null) }
    }

    fun toggle() {
        if (AudioPlayerHelper.isActiveFile(fileId)) {
            if (AudioPlayerHelper.isPlaying()) AudioPlayerHelper.pause() else AudioPlayerHelper.resume()
            return
        }
        loading = true
        scope.launch {
            val file = container.chatRepository.downloadFile(fileId)
            loading = false
            if (file != null) AudioPlayerHelper.play(fileId, file, callbacks)
        }
    }

    val accent = if (mine) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.primary
    val onAccent = if (mine) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onPrimary
    val secondary = if (mine) MaterialTheme.colorScheme.onPrimary.copy(alpha = 0.7f)
    else MaterialTheme.colorScheme.onSurfaceVariant
    val title = if (mine) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurface

    Row(
        modifier = Modifier.padding(bottom = 4.dp).widthIn(min = 220.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(
            modifier = Modifier
                .size(44.dp)
                .clip(CircleShape)
                .background(accent)
                .clickable(enabled = !loading) { toggle() },
            contentAlignment = Alignment.Center
        ) {
            when {
                loading -> CircularProgressIndicator(
                    modifier = Modifier.size(22.dp), color = onAccent, strokeWidth = 2.dp
                )
                isPlaying -> Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    repeat(2) {
                        Box(
                            Modifier.size(width = 4.dp, height = 16.dp)
                                .background(onAccent, RoundedCornerShape(1.dp))
                        )
                    }
                }
                else -> Icon(Icons.Filled.PlayArrow, contentDescription = "Воспроизвести", tint = onAccent)
            }
        }
        Spacer(Modifier.width(12.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = fileName.ifBlank { "Голосовое сообщение" },
                style = MaterialTheme.typography.bodyMedium,
                color = title,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            Slider(
                value = if (durationMs > 0) (positionMs.toFloat() / durationMs).coerceIn(0f, 1f) else 0f,
                onValueChange = { frac ->
                    if (AudioPlayerHelper.isActiveFile(fileId) && durationMs > 0) {
                        val target = (frac * durationMs).toInt()
                        positionMs = target
                        AudioPlayerHelper.seekTo(target)
                    }
                },
                colors = SliderDefaults.colors(
                    thumbColor = accent,
                    activeTrackColor = accent,
                    inactiveTrackColor = secondary.copy(alpha = 0.4f)
                ),
                modifier = Modifier.fillMaxWidth()
            )
            Text(
                text = formatAudioTime(positionMs) + " / " + formatAudioTime(durationMs),
                style = MaterialTheme.typography.labelSmall,
                color = secondary
            )
        }
    }
}

private fun formatAudioTime(ms: Int): String {
    if (ms <= 0) return "0:00"
    val totalSec = ms / 1000
    return "%d:%02d".format(totalSec / 60, totalSec % 60)
}
