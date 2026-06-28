package com.barkfluff.clientv2.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.rememberTransformableState
import androidx.compose.foundation.gestures.transformable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import coil.compose.AsyncImage
import com.barkfluff.clientv2.di.LocalAppContainer

/** Картинка для просмотра: fileId (полный файл) + previewUrl (мгновенный показ до загрузки). */
data class ImageItem(val fileId: String, val previewUrl: String)

/**
 * Полноэкранный просмотрщик изображений: пейджер между картинками сообщения + pinch-to-zoom.
 * Полный файл резолвится через getFileDownloadUrl; до загрузки показывается previewUrl.
 */
@Composable
fun ImageViewerOverlay(items: List<ImageItem>, startIndex: Int, onDismiss: () -> Unit) {
    if (items.isEmpty()) return
    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false)
    ) {
        val pagerState = rememberPagerState(
            initialPage = startIndex.coerceIn(0, items.lastIndex),
            pageCount = { items.size }
        )
        Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
            HorizontalPager(state = pagerState, modifier = Modifier.fillMaxSize()) { page ->
                ZoomableImage(items[page])
            }
            Box(
                modifier = Modifier.fillMaxWidth().align(Alignment.TopCenter).padding(8.dp)
            ) {
                IconButton(onClick = onDismiss, modifier = Modifier.align(Alignment.CenterStart)) {
                    Icon(Icons.Filled.Close, contentDescription = "Закрыть", tint = Color.White)
                }
                if (items.size > 1) {
                    Text(
                        text = "${pagerState.currentPage + 1} / ${items.size}",
                        color = Color.White,
                        style = MaterialTheme.typography.labelLarge,
                        modifier = Modifier.align(Alignment.Center)
                    )
                }
            }
        }
    }
}

@Composable
private fun ZoomableImage(item: ImageItem) {
    val container = LocalAppContainer.current
    var fullUrl by remember(item.fileId) { mutableStateOf<String?>(null) }

    androidx.compose.runtime.LaunchedEffect(item.fileId) {
        if (item.fileId.isNotBlank()) {
            fullUrl = container.grpcManager.getFileDownloadUrl(item.fileId).getOrNull()
        }
    }

    var scale by remember { mutableStateOf(1f) }
    var offset by remember { mutableStateOf(Offset.Zero) }
    val transformState = rememberTransformableState { zoomChange, panChange, _ ->
        scale = (scale * zoomChange).coerceIn(1f, 5f)
        offset = if (scale > 1f) offset + panChange else Offset.Zero
    }

    AsyncImage(
        model = fullUrl ?: item.previewUrl.ifBlank { null },
        contentDescription = null,
        contentScale = ContentScale.Fit,
        modifier = Modifier
            .fillMaxSize()
            .graphicsLayer(
                scaleX = scale,
                scaleY = scale,
                translationX = offset.x,
                translationY = offset.y
            )
            .transformable(transformState)
    )
}
