package com.barkfluff.clientv2.ui.components

import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import com.barkfluff.clientv2.di.LocalAppContainer

/**
 * Вложение-GIF: анимируется через app-wide Coil ImageLoader (ImageDecoderDecoder).
 * Полный файл резолвится по fileId (как у видео); до этого показывается превью.
 */
@Composable
fun GifAttachment(fileId: String, previewUrl: String) {
    val container = LocalAppContainer.current
    var url by remember(fileId) { mutableStateOf(previewUrl.ifBlank { null }) }

    LaunchedEffect(fileId) {
        if (fileId.isNotBlank()) {
            container.grpcManager.getFileDownloadUrl(fileId).getOrNull()?.let { url = it }
        }
    }

    AsyncImage(
        model = url,
        contentDescription = null,
        contentScale = ContentScale.Crop,
        modifier = Modifier
            .padding(bottom = 4.dp)
            .size(220.dp)
            .clip(RoundedCornerShape(12.dp))
    )
}
