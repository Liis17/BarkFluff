package com.barkfluff.client.adapter

import android.content.res.ColorStateList
import android.graphics.Color
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import com.barkfluff.client.data.ServerDataElement
import com.google.android.material.card.MaterialCardView
import com.google.android.material.chip.Chip
import com.google.android.material.color.MaterialColors
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch

/**
 * Адаптер для списка серверов
 */
class ServerAdapter(
    private val coroutineScope: CoroutineScope,
    private val measurePing: suspend (String) -> Int?,
    private val onServerClick: (ServerDataElement) -> Unit
) : ListAdapter<ServerDataElement, ServerAdapter.ServerViewHolder>(ServerDiffCallback()) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ServerViewHolder {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_server, parent, false)
        return ServerViewHolder(view, onServerClick)
    }

    override fun onBindViewHolder(holder: ServerViewHolder, position: Int) {
        holder.bind(getItem(position), coroutineScope, measurePing)
    }

    override fun onViewRecycled(holder: ServerViewHolder) {
        super.onViewRecycled(holder)
        holder.cancelPendingPing()
    }

    class ServerViewHolder(
        itemView: View,
        private val onServerClick: (ServerDataElement) -> Unit
    ) : RecyclerView.ViewHolder(itemView) {

        private val serverIconTile: MaterialCardView = itemView.findViewById(R.id.serverIconTile)
        private val title: TextView = itemView.findViewById(R.id.serverTitle)
        private val description: TextView = itemView.findViewById(R.id.serverDescription)
        private val handle: TextView = itemView.findViewById(R.id.serverHandle)
        private val chipOnline: Chip = itemView.findViewById(R.id.chipOnline)
        private val chipPing: Chip = itemView.findViewById(R.id.chipPing)
        private val chipRegion: Chip = itemView.findViewById(R.id.chipRegion)
        private val connectCta = itemView.findViewById<com.google.android.material.button.MaterialButton>(R.id.serverConnectCta)

        private var pingJob: Job? = null

        private enum class ServerStatus {
            CHECKING,
            ONLINE,
            UNAVAILABLE,
        }

        fun cancelPendingPing() {
            pingJob?.cancel()
            pingJob = null
        }

        fun bind(server: ServerDataElement, coroutineScope: CoroutineScope, measurePing: suspend (String) -> Int?) {
            cancelPendingPing()
            itemView.tag = server.ip
            title.text = server.title
            description.text = server.description

            // Чипы информируют о состоянии, но не являются отдельными действиями.
            listOf(chipOnline, chipPing, chipRegion).forEach { chip ->
                chip.isClickable = false
                chip.isFocusable = false
                chip.isFocusableInTouchMode = false
                chip.importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_YES
            }

            // Макет 2c: регион — чип в общей строке, публичное имя — отдельная строка ниже
            if (server.location.isNotBlank()) {
                chipRegion.text = server.location
                chipRegion.visibility = View.VISIBLE
            } else {
                chipRegion.visibility = View.GONE
            }

            if (server.publicName.isNotBlank()) {
                handle.text = itemView.context.getString(R.string.server_item_handle, server.publicName)
                handle.visibility = View.VISIBLE
            } else {
                handle.visibility = View.GONE
            }

            // Цвет icon-tile: своё значение ноды, иначе — primary активной темы
            val defaultColor = MaterialColors.getColor(
                itemView, androidx.appcompat.R.attr.colorPrimary
            )
            try {
                if (server.hexColor.isNotBlank()) {
                    val color = Color.parseColor(if (server.hexColor.startsWith("#")) server.hexColor else "#${server.hexColor}")
                    serverIconTile.setCardBackgroundColor(color)
                } else {
                    serverIconTile.setCardBackgroundColor(defaultColor)
                }
            } catch (e: Exception) {
                serverIconTile.setCardBackgroundColor(defaultColor)
            }

            chipOnline.visibility = View.VISIBLE
            chipPing.visibility = View.GONE
            setStatus(ServerStatus.CHECKING)

            // Единственное действие карточки — явная кнопка подключения.
            connectCta.setOnClickListener {
                onServerClick(server)
            }

            // Probe: защита от гонки при recycle через itemView.tag sentinel.
            pingJob = coroutineScope.launch {
                val ms = measurePing(server.ip)
                if (itemView.tag == server.ip) {
                    if (ms != null) {
                        setStatus(ServerStatus.ONLINE)
                        chipPing.text = itemView.context.getString(R.string.server_response_ms, ms)
                        chipPing.visibility = View.VISIBLE
                    } else {
                        setStatus(ServerStatus.UNAVAILABLE)
                        chipPing.visibility = View.GONE
                    }
                }
            }
        }

        private fun setStatus(status: ServerStatus) {
            val (backgroundColor, contentColor) = when (status) {
                ServerStatus.CHECKING -> MaterialColors.getColor(
                    itemView,
                    com.google.android.material.R.attr.colorSurfaceContainerHighest
                ) to MaterialColors.getColor(
                    itemView,
                    com.google.android.material.R.attr.colorOnSurfaceVariant
                )

                ServerStatus.ONLINE -> itemView.context.getColor(R.color.onboarding_success_background) to
                    itemView.context.getColor(R.color.onboarding_success_text)

                ServerStatus.UNAVAILABLE -> MaterialColors.getColor(
                    itemView,
                    com.google.android.material.R.attr.colorErrorContainer
                ) to MaterialColors.getColor(
                    itemView,
                    com.google.android.material.R.attr.colorOnErrorContainer
                )
            }

            chipOnline.text = itemView.context.getString(
                when (status) {
                    ServerStatus.CHECKING -> R.string.server_status_checking
                    ServerStatus.ONLINE -> R.string.server_status_online
                    ServerStatus.UNAVAILABLE -> R.string.server_status_unavailable
                }
            )
            chipOnline.setChipBackgroundColor(ColorStateList.valueOf(backgroundColor))
            chipOnline.setTextColor(contentColor)
            chipOnline.chipIconTint = ColorStateList.valueOf(contentColor)
        }
    }

    private class ServerDiffCallback : DiffUtil.ItemCallback<ServerDataElement>() {
        override fun areItemsTheSame(oldItem: ServerDataElement, newItem: ServerDataElement): Boolean {
            return oldItem.ip == newItem.ip
        }

        override fun areContentsTheSame(oldItem: ServerDataElement, newItem: ServerDataElement): Boolean {
            return oldItem == newItem
        }
    }
}
