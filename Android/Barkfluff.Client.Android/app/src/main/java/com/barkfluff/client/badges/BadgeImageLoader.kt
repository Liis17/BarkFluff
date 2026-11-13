package com.barkfluff.client.badges

import android.widget.ImageView
import coil.annotation.ExperimentalCoilApi
import coil.request.ImageRequest
import com.barkfluff.client.R
import com.barkfluff.client.domain.model.UserBadgeInfo
import com.barkfluff.client.utils.AvatarLoader

internal object BadgeImageLoader {
    @OptIn(ExperimentalCoilApi::class)
    fun load(imageView: ImageView, url: String) {
        if (url.isBlank()) {
            imageView.setImageResource(R.drawable.ic_verified)
            return
        }
        imageView.setImageResource(R.drawable.ic_verified)
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
}
