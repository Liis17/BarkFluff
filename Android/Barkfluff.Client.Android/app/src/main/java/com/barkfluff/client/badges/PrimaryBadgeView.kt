package com.barkfluff.client.badges

import android.content.Context
import android.content.ContextWrapper
import android.view.View
import android.widget.ImageView
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.lifecycleScope
import coil.dispose
import com.barkfluff.client.R
import com.barkfluff.client.cache.UserBadgeRepository
import com.barkfluff.client.domain.model.UserBadgeInfo
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.android.EntryPointAccessors
import dagger.hilt.components.SingletonComponent
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch

/**
 * Миниатюра основного бейджа рядом с именем пользователя в списках, шапке чата и сообщениях.
 * Значок не кликабелен: просмотр бейджа доступен в профиле.
 */
object PrimaryBadgeView {
    @EntryPoint
    @InstallIn(SingletonComponent::class)
    interface Dependencies {
        fun userBadgeRepository(): UserBadgeRepository
    }

    /**
     * Показывает основной бейдж [userId] в [imageView] или скрывает его (`userId <= 0`, нет бейджа).
     * Безопасен для переиспользуемых строк RecyclerView: результат устаревшего запроса не применяется.
     */
    fun bind(imageView: ImageView, userId: Long) {
        (imageView.getTag(R.id.tag_primary_badge_job) as? Job)?.cancel()
        imageView.setTag(R.id.tag_primary_badge_user, userId)
        if (userId <= 0L) {
            render(imageView, null)
            return
        }
        val repository = repository(imageView.context)
        render(imageView, repository.peekPrimary(userId))
        val owner = imageView.context.lifecycleOwner() ?: return
        val job = owner.lifecycleScope.launch {
            val cached = repository.cachedPrimary(userId)
            if (imageView.getTag(R.id.tag_primary_badge_user) == userId) render(imageView, cached)
            val fresh = repository.refreshPrimary(userId)
            if (imageView.getTag(R.id.tag_primary_badge_user) == userId) render(imageView, fresh)
        }
        imageView.setTag(R.id.tag_primary_badge_job, job)
    }

    private fun render(imageView: ImageView, badge: UserBadgeInfo?) {
        if (badge == null) {
            imageView.setTag(R.id.tag_primary_badge_url, null)
            imageView.dispose()
            imageView.setImageDrawable(null)
            imageView.contentDescription = null
            imageView.visibility = View.GONE
            return
        }
        val url = badge.miniImageUrl.ifBlank { badge.imageUrl }
        if (url.isBlank()) return render(imageView, null)
        imageView.contentDescription = badge.name
        imageView.visibility = View.VISIBLE
        // Повторный bind той же строки не перезапускает загрузку и crossfade.
        if (imageView.getTag(R.id.tag_primary_badge_url) == url) return
        imageView.setTag(R.id.tag_primary_badge_url, url)
        BadgeImageLoader.load(imageView, url)
    }

    internal fun repository(context: Context): UserBadgeRepository =
        EntryPointAccessors.fromApplication(context.applicationContext, Dependencies::class.java)
            .userBadgeRepository()

    private tailrec fun Context.lifecycleOwner(): LifecycleOwner? = when (this) {
        is LifecycleOwner -> this
        is ContextWrapper -> baseContext.lifecycleOwner()
        else -> null
    }
}

/** Compose-вариант [PrimaryBadgeView]: без основного бейджа ничего не рисует и не занимает места. */
@Composable
fun PrimaryBadgeIcon(userId: Long, modifier: Modifier = Modifier, size: Dp = 18.dp) {
    val context = LocalContext.current
    val repository = remember(context) { PrimaryBadgeView.repository(context) }
    var badge by remember(userId) { mutableStateOf(repository.peekPrimary(userId)) }
    LaunchedEffect(userId) {
        badge = repository.cachedPrimary(userId)
        badge = repository.refreshPrimary(userId)
    }
    val current = badge ?: return
    val url = current.miniImageUrl.ifBlank { current.imageUrl }
    if (url.isBlank()) return
    key(url) {
        AndroidView(
            factory = { viewContext ->
                ImageView(viewContext).apply {
                    scaleType = ImageView.ScaleType.FIT_CENTER
                    BadgeImageLoader.load(this, url)
                }
            },
            update = { it.contentDescription = current.name },
            modifier = modifier.size(size),
        )
    }
}
