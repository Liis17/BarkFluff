package com.barkfluff.client.adapter

import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import com.barkfluff.client.utils.ImageLoadHelper
import com.github.chrisbanes.photoview.PhotoView

/**
 * Адаптер ViewPager2 для полноэкранного просмотра изображений.
 * Каждая страница содержит PhotoView с поддержкой масштабирования.
 */
class ImagePagerAdapter(
    private val fileIds: List<String>,
    private val previewUrls: List<String> = emptyList(),
    private val getUrlCallback: suspend (String) -> String?
) : RecyclerView.Adapter<ImagePagerAdapter.ImageViewHolder>() {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ImageViewHolder {
        val container = FrameLayout(parent.context).apply {
            layoutParams = ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            )
        }

        val photoView = PhotoView(parent.context).apply {
            layoutParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT
            )
            maximumScale = 5f
            mediumScale = 2.5f
        }
        container.addView(photoView)

        val progress = com.google.android.material.progressindicator.CircularProgressIndicator(parent.context).apply {
            layoutParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.WRAP_CONTENT,
                FrameLayout.LayoutParams.WRAP_CONTENT
            ).apply {
                gravity = android.view.Gravity.CENTER
            }
            isIndeterminate = true
            visibility = View.VISIBLE
        }
        container.addView(progress)

        return ImageViewHolder(container, photoView, progress)
    }

    override fun onBindViewHolder(holder: ImageViewHolder, position: Int) {
        val previewUrl = previewUrls.getOrNull(position) ?: ""
        holder.bind(fileIds[position], previewUrl)
    }

    override fun getItemCount() = fileIds.size

    inner class ImageViewHolder(
        container: View,
        private val photoView: PhotoView,
        private val progress: View
    ) : RecyclerView.ViewHolder(container) {

        fun bind(fileId: String, previewUrl: String) {
            progress.visibility = View.VISIBLE
            photoView.setImageDrawable(null)

            // Use preview URL directly if available, fall back to GetTempDownloadUrl
            val getUrl: suspend () -> String? = if (previewUrl.isNotBlank()) {
                { previewUrl }
            } else {
                { getUrlCallback(fileId) }
            }

            ImageLoadHelper.loadByFileId(
                imageView = photoView,
                fileId = fileId,
                getUrlCallback = getUrl,
                onSuccess = { progress.visibility = View.GONE },
                onError = {
                    progress.visibility = View.GONE
                    photoView.setImageResource(R.drawable.ic_image_placeholder)
                }
            )
        }
    }
}
