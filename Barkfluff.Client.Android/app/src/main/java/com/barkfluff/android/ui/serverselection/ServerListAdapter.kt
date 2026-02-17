package com.barkfluff.android.ui.serverselection

import android.graphics.Color
import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.android.data.model.ServerInfo
import com.barkfluff.android.databinding.ItemServerBinding

class ServerListAdapter(
    private val onServerClick: (ServerInfo) -> Unit,
) : ListAdapter<ServerInfo, ServerListAdapter.ViewHolder>(DiffCallback) {

    inner class ViewHolder(private val binding: ItemServerBinding) :
        RecyclerView.ViewHolder(binding.root) {

        fun bind(server: ServerInfo) {
            binding.tvServerName.text = server.name
            binding.tvServerDescription.text = server.description
            binding.tvServerMeta.text = buildString {
                if (server.location.isNotEmpty()) append("${server.location} • ")
                append("${server.accountsCount} аккаунтов")
            }

            try {
                val color = Color.parseColor(
                    if (server.colorMain.startsWith("#")) server.colorMain
                    else "#${server.colorMain}"
                )
                binding.viewColorDot.setBackgroundColor(color)
            } catch (_: IllegalArgumentException) {
                binding.viewColorDot.setBackgroundColor(Color.parseColor("#6200EE"))
            }

            binding.root.setOnClickListener { onServerClick(server) }
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemServerBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    companion object DiffCallback : DiffUtil.ItemCallback<ServerInfo>() {
        override fun areItemsTheSame(oldItem: ServerInfo, newItem: ServerInfo): Boolean =
            oldItem.beaconHost == newItem.beaconHost && oldItem.beaconPort == newItem.beaconPort

        override fun areContentsTheSame(oldItem: ServerInfo, newItem: ServerInfo): Boolean =
            oldItem == newItem
    }
}
