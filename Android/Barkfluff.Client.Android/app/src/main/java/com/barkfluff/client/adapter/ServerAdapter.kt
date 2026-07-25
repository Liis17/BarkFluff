package com.barkfluff.client.adapter

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

        private val card: MaterialCardView = itemView.findViewById(R.id.serverCard)
        private val serverIconTile: MaterialCardView = itemView.findViewById(R.id.serverIconTile)
        private val title: TextView = itemView.findViewById(R.id.serverTitle)
        private val description: TextView = itemView.findViewById(R.id.serverDescription)
        private val handle: TextView = itemView.findViewById(R.id.serverHandle)
        private val chipOnline: Chip = itemView.findViewById(R.id.chipOnline)
        private val chipPing: Chip = itemView.findViewById(R.id.chipPing)
        private val chipRegion: Chip = itemView.findViewById(R.id.chipRegion)
        private val connectCta = itemView.findViewById<com.google.android.material.button.MaterialButton>(R.id.serverConnectCta)

        private var pingJob: Job? = null

        fun cancelPendingPing() {
            pingJob?.cancel()
            pingJob = null
        }

        fun bind(server: ServerDataElement, coroutineScope: CoroutineScope, measurePing: suspend (String) -> Int?) {
            title.text = server.title
            description.text = server.description

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

            // Сервер уже гарантированно жив (Navigator не вернул бы мёртвый сервер)
            chipOnline.visibility = View.VISIBLE

            // Обработчики клика
            card.setOnClickListener {
                onServerClick(server)
            }
            connectCta.setOnClickListener {
                onServerClick(server)
            }

            // Пинг: защита от гонки при recycle через itemView.tag sentinel
            cancelPendingPing()
            itemView.tag = server.ip
            chipPing.visibility = View.GONE
            pingJob = coroutineScope.launch {
                val ms = measurePing(server.ip)
                if (itemView.tag == server.ip) {
                    if (ms != null) {
                        chipPing.text = itemView.context.getString(R.string.server_ping_ms, ms)
                        chipPing.visibility = View.VISIBLE
                    } else {
                        chipPing.visibility = View.GONE
                    }
                }
            }
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
