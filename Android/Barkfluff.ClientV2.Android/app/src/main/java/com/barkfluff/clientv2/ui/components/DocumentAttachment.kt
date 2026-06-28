package com.barkfluff.clientv2.ui.components

import android.content.Context
import android.content.Intent
import android.webkit.MimeTypeMap
import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.content.FileProvider
import com.barkfluff.client.utils.FileCache
import com.barkfluff.clientv2.di.LocalAppContainer
import kotlinx.coroutines.launch
import java.io.File

/**
 * Вложение-документ (DOCUMENT): плитка с расширением файла + имя/размер. Тап скачивает файл
 * (кэш в FileCache) и открывает системным выбором приложений через FileProvider.
 */
@Composable
fun DocumentAttachment(fileId: String, fileName: String, sizeBytes: Long, mine: Boolean) {
    val container = LocalAppContainer.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var downloading by remember(fileId) { mutableStateOf(false) }

    val accent = if (mine) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.primary
    val onAccent = if (mine) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onPrimary
    val title = if (mine) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurface
    val secondary = if (mine) MaterialTheme.colorScheme.onPrimary.copy(alpha = 0.7f)
    else MaterialTheme.colorScheme.onSurfaceVariant

    fun onTap() {
        if (downloading) return
        val cached = FileCache.getFile(fileId)
        if (cached != null) {
            openFile(context, cached, fileName)
            return
        }
        downloading = true
        scope.launch {
            val file = container.chatRepository.downloadFile(fileId)
            downloading = false
            if (file != null) openFile(context, file, fileName)
            else Toast.makeText(context, "Не удалось загрузить файл", Toast.LENGTH_SHORT).show()
        }
    }

    Row(
        modifier = Modifier
            .padding(bottom = 4.dp)
            .widthIn(min = 200.dp)
            .clickable { onTap() },
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(
            modifier = Modifier
                .size(44.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(accent),
            contentAlignment = Alignment.Center
        ) {
            if (downloading) {
                CircularProgressIndicator(modifier = Modifier.size(22.dp), color = onAccent, strokeWidth = 2.dp)
            } else {
                Text(
                    text = fileExtension(fileName),
                    style = MaterialTheme.typography.labelSmall,
                    color = onAccent
                )
            }
        }
        Spacer(Modifier.width(12.dp))
        Column(modifier = Modifier.width(180.dp)) {
            Text(
                text = fileName.ifBlank { "Документ" },
                style = MaterialTheme.typography.bodyMedium,
                color = title,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            val size = formatFileSize(sizeBytes)
            if (size.isNotEmpty()) {
                Text(text = size, style = MaterialTheme.typography.labelSmall, color = secondary)
            }
        }
    }
}

private fun openFile(context: Context, file: File, fileName: String) {
    try {
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
        val mime = MimeTypeMap.getSingleton()
            .getMimeTypeFromExtension(fileName.substringAfterLast('.', "")) ?: "application/octet-stream"
        val intent = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, mime)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        context.startActivity(Intent.createChooser(intent, "Открыть с помощью"))
    } catch (_: Exception) {
        Toast.makeText(context, "Не удалось открыть файл", Toast.LENGTH_SHORT).show()
    }
}

private fun fileExtension(fileName: String): String {
    val ext = fileName.substringAfterLast('.', "").uppercase()
    return if (ext.isBlank()) "FILE" else ext.take(4)
}

private fun formatFileSize(bytes: Long): String = when {
    bytes <= 0 -> ""
    bytes < 1024 -> "$bytes B"
    bytes < 1024 * 1024 -> "%.1f KB".format(bytes / 1024f)
    bytes < 1024 * 1024 * 1024 -> "%.1f MB".format(bytes / (1024f * 1024f))
    else -> "%.1f GB".format(bytes / (1024f * 1024f * 1024f))
}
