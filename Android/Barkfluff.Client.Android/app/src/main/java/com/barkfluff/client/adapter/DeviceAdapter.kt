package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.ItemDeviceBinding
import com.barkfluff.client.grpc.GrpcManager

class DeviceAdapter(
    private val onItemClick: (GrpcManager.SessionData) -> Unit
) : ListAdapter<GrpcManager.SessionData, DeviceAdapter.DeviceViewHolder>(DeviceDiffCallback()) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): DeviceViewHolder {
        val binding = ItemDeviceBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return DeviceViewHolder(binding)
    }

    override fun onBindViewHolder(holder: DeviceViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    inner class DeviceViewHolder(
        private val binding: ItemDeviceBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(session: GrpcManager.SessionData) {
            val deviceName = session.customName.ifEmpty { session.originalName }
            binding.textDeviceName.text = deviceName.ifEmpty { "Неизвестное устройство" }
            binding.textDeviceInfo.text = session.appName

            binding.root.setOnClickListener {
                onItemClick(session)
            }
        }
    }

    class DeviceDiffCallback : DiffUtil.ItemCallback<GrpcManager.SessionData>() {
        override fun areItemsTheSame(oldItem: GrpcManager.SessionData, newItem: GrpcManager.SessionData): Boolean {
            return oldItem.id == newItem.id
        }

        override fun areContentsTheSame(oldItem: GrpcManager.SessionData, newItem: GrpcManager.SessionData): Boolean {
            return oldItem == newItem
        }
    }
}
