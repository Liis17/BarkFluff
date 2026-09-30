package com.barkfluff.client.adapter

import android.view.View
import android.view.ViewTreeObserver
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.recyclerview.widget.RecyclerView
import barkfluff.shared.Shared
import com.barkfluff.client.data.AutoDownloadSettingsStore
import com.barkfluff.client.domain.media.AutoDownloadNetworkState
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch

/** View/lifecycle bridge shared by message rows and profile/gallery rows. */
class AttachmentAutoDownloadViews(
    loader: AttachmentLoader,
    owner: LifecycleOwner,
    settings: AutoDownloadSettingsStore,
    network: AutoDownloadNetworkState,
) {
    private val controller = AttachmentAutoDownloadController(loader, owner.lifecycleScope)
    private val views = mutableMapOf<View, AttachmentAutoDownloadController.Binding>()
    private var recyclerView: RecyclerView? = null
    private val layoutListener = ViewTreeObserver.OnGlobalLayoutListener { refresh() }
    private val scrollListener = object : RecyclerView.OnScrollListener() {
        override fun onScrolled(recyclerView: RecyclerView, dx: Int, dy: Int) { refresh() }
    }
    private val attachListener = object : View.OnAttachStateChangeListener {
        override fun onViewAttachedToWindow(view: View) { refresh() }
        override fun onViewDetachedFromWindow(view: View) { views[view]?.setVisible(false) }
    }

    init {
        controller.update(settings.settings.value, network.network.value)
        owner.lifecycleScope.launch {
            owner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                controller.update(settings.settings.value, network.network.value)
                refresh()
                controller.setActive(true)
                try {
                    combine(settings.settings, network.network) { config, connection -> config to connection }
                        .collect { (config, connection) -> controller.update(config, connection) }
                } finally {
                    controller.setActive(false)
                }
            }
        }
    }

    fun bind(view: View, attachment: Shared.MessageAttachment, onState: (AttachmentDownloadState) -> Unit) {
        views.remove(view)?.close()
        view.removeOnAttachStateChangeListener(attachListener)
        views[view] = controller.bind(attachment, onState)
        view.addOnAttachStateChangeListener(attachListener)
        refresh()
    }

    fun attach(recyclerView: RecyclerView) {
        this.recyclerView = recyclerView
        recyclerView.viewTreeObserver.addOnGlobalLayoutListener(layoutListener)
        recyclerView.addOnScrollListener(scrollListener)
        refresh()
    }

    fun detach(recyclerView: RecyclerView) {
        if (recyclerView.viewTreeObserver.isAlive) recyclerView.viewTreeObserver.removeOnGlobalLayoutListener(layoutListener)
        recyclerView.removeOnScrollListener(scrollListener)
        this.recyclerView = null
        views.keys.toList().forEach(::remove)
    }

    fun recycleTree(root: View) {
        views.keys.filter { it === root || isDescendant(it, root) }.forEach(::remove)
    }

    fun setManualDownloading(view: View, value: Boolean) {
        views[view]?.setManualDownloading(value)
    }

    fun refresh() {
        views.toMap().forEach { (view, binding) ->
            val parent = recyclerView
            val visible = parent != null && view.isAttachedToWindow && view.isShown &&
                view.getGlobalVisibleRect(android.graphics.Rect())
            binding.setVisible(visible)
        }
        controller.refresh()
    }

    private fun remove(view: View) {
        views.remove(view)?.close()
        view.removeOnAttachStateChangeListener(attachListener)
    }

    private fun isDescendant(view: View, root: View): Boolean {
        var parent = view.parent
        while (parent is View) {
            if (parent === root) return true
            parent = parent.parent
        }
        return false
    }
}
