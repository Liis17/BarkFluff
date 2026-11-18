package com.barkfluff.client.badges

import android.widget.ImageView
import android.content.res.ColorStateList
import coil.annotation.ExperimentalCoilApi
import coil.request.ImageRequest
import androidx.core.content.ContextCompat
import com.barkfluff.client.R
import com.barkfluff.client.domain.model.UserBadgeInfo
import com.barkfluff.client.utils.AvatarLoader

internal object BadgeImageLoader {
    @OptIn(ExperimentalCoilApi::class)
    fun load(imageView: ImageView, url: String) {
        if (url.isBlank()) {
            showNoBadge(imageView)
            return
        }
        imageView.imageTintList = null
        imageView.setImageDrawable(null)
        val request = ImageRequest.Builder(imageView.context)
            .data(url)
            .memoryCacheKey(url)
            .diskCacheKey(url)
            .crossfade(180)
            .target(imageView)
            .build()
        AvatarLoader.getImageLoader(imageView.context).enqueue(request)
    }

    fun loadMini(imageView: ImageView, badge: UserBadgeInfo) {
        load(imageView, badge.miniImageUrl.ifBlank { badge.imageUrl })
    }

    fun showNoBadge(imageView: ImageView) {
        imageView.setImageResource(R.drawable.ic_badge_none)
        imageView.contentDescription = imageView.context.getString(R.string.badge_no_primary)
        imageView.imageTintList = ColorStateList.valueOf(
            ContextCompat.getColor(imageView.context, R.color.profile_settings_on_surface_variant)
        )
    }
}
