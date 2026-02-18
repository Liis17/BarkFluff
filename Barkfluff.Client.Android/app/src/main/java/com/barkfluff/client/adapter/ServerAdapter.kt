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

/**
 * Адаптер для списка серверов
 */
class ServerAdapter(
    private val onServerClick: (ServerDataElement) -> Unit
) : ListAdapter<ServerDataElement, ServerAdapter.ServerViewHolder>(ServerDiffCallback()) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ServerViewHolder {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_server, parent, false)
        return ServerViewHolder(view, onServerClick)
    }

    override fun onBindViewHolder(holder: ServerViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    class ServerViewHolder(
        itemView: View,
        private val onServerClick: (ServerDataElement) -> Unit
    ) : RecyclerView.ViewHolder(itemView) {

        private val card: MaterialCardView = itemView.findViewById(R.id.serverCard)
        private val colorIndicator: View = itemView.findViewById(R.id.serverColorIndicator)
        private val title: TextView = itemView.findViewById(R.id.serverTitle)
        private val description: TextView = itemView.findViewById(R.id.serverDescription)
        private val locationAndPublicName: TextView = itemView.findViewById(R.id.serverLocationAndPublicName)

        fun bind(server: ServerDataElement) {
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

            // Устанавливаем цветовой индикатор
            try {
                if (server.hexColor.isNotBlank()) {
                    val color = Color.parseColor(if (server.hexColor.startsWith("#")) server.hexColor else "#${server.hexColor}")
                    colorIndicator.setBackgroundColor(color)
                } else {
                    // Цвет по умолчанию
                    colorIndicator.setBackgroundColor(Color.parseColor("#FF6B35"))
                }
            } catch (e: Exception) {
                // Используем цвет по умолчанию при ошибке
                colorIndicator.setBackgroundColor(Color.parseColor("#FF6B35"))
            }

            // Обработчик клика
            card.setOnClickListener {
                onServerClick(server)
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
