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
        private val locationAndPublicName: TextView = itemView.findViewById(R.id.serverLocationAndPublicName)
        private val chipOnline: Chip = itemView.findViewById(R.id.chipOnline)
        private val chipPing: Chip = itemView.findViewById(R.id.chipPing)
        private val connectCta = itemView.findViewById<com.google.android.material.button.MaterialButton>(R.id.serverConnectCta)

        private var pingJob: Job? = null

        fun cancelPendingPing() {
            pingJob?.cancel()
            pingJob = null
        }

        fun bind(server: ServerDataElement, coroutineScope: CoroutineScope, measurePing: suspend (String) -> Int?) {
            title.text = server.title
            description.text = server.description

            // Формируем строку локации и публичного имени
            val locationText = buildString {
                if (server.location.isNotBlank()) {
                    append(server.location)
                }
                if (server.publicName.isNotBlank()) {
                    if (isNotEmpty()) {
                        append(" • ")
                    }
                    append("@${server.publicName}")
                }
            }

            if (locationText.isNotBlank()) {
                locationAndPublicName.text = locationText
                locationAndPublicName.visibility = View.VISIBLE
            } else {
                locationAndPublicName.visibility = View.GONE
            }

            // Цвет icon-tile
            val defaultColor = androidx.core.content.ContextCompat.getColor(
                itemView.context, R.color.onboarding_button_background
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
