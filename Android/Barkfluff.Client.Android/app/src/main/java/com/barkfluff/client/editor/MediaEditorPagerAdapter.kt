package com.barkfluff.client.editor

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.PageMediaEditorImageBinding
import com.github.chrisbanes.photoview.PhotoView
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Адаптер ViewPager2 для редактора картинок.
 * Каждая страница — full-screen FrameLayout с PhotoView и контейнерами для tools.
 * При биндинге грузит bitmap из MediaEditCache (если есть правка) или из URI.
 */
class MediaEditorPagerAdapter(
    private val uris: List<Uri>,
    private val loadBitmap: suspend (Uri) -> Bitmap?
) : RecyclerView.Adapter<MediaEditorPagerAdapter.PageHolder>() {

    private val scope = CoroutineScope(Dispatchers.Main + Job())
    private val loadJobs = mutableMapOf<Int, Job>()

    inner class PageHolder(val binding: PageMediaEditorImageBinding) :
        RecyclerView.ViewHolder(binding.root) {
        val photoView: PhotoView get() = binding.photoView
        val cropContainer: ViewGroup get() = binding.cropContainer
        val drawingOverlay: DrawingOverlayView get() = binding.drawingOverlay
        var currentBitmap: Bitmap? = null
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): PageHolder {
        val binding = PageMediaEditorImageBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return PageHolder(binding)
    }

    override fun getItemCount(): Int = uris.size

    override fun onBindViewHolder(holder: PageHolder, position: Int) {
        val uri = uris[position]
        loadJobs[position]?.cancel()
        // Сначала пробуем кеш правок
        val edited = MediaEditCache.get(uri)?.bytes
        if (edited != null) {
            val bmp = withContextDecode(edited)
            holder.currentBitmap = bmp
            holder.photoView.setImageBitmap(bmp)
            return
        }
        // Иначе грузим оригинал асинхронно
        holder.photoView.setImageDrawable(null)
        holder.currentBitmap = null
        loadJobs[position] = scope.launch {
            val bmp = loadBitmap(uri)
            if (holder.bindingAdapterPosition == position) {
                holder.currentBitmap = bmp
                if (bmp != null) holder.photoView.setImageBitmap(bmp)
            }
        }
    }

    override fun onViewRecycled(holder: PageHolder) {
        super.onViewRecycled(holder)
        holder.currentBitmap = null
        holder.photoView.setImageDrawable(null)
        holder.cropContainer.removeAllViews()
        holder.cropContainer.visibility = ViewGroup.GONE
        holder.drawingOverlay.visibility = ViewGroup.GONE
        holder.drawingOverlay.setSourceBitmap(null)
    }

    fun cancelAll() {
        loadJobs.values.forEach { it.cancel() }
        loadJobs.clear()
        scope.coroutineContext[Job]?.cancel()
    }

    private fun withContextDecode(bytes: ByteArray): Bitmap? {
        return try {
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
        } catch (e: Exception) {
            null
        }
    }
}
