package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.ItemDeviceBinding
import com.barkfluff.client.grpc.GrpcManager

class DeviceAdapter(
    private val currentDeviceId: String,
    private val onRemoveClick: (GrpcManager.SessionData) -> Unit
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
            binding.textDeviceInfo.text = "${session.appName} \u00B7 ${session.os}"
            binding.textDeviceLocation.text = session.location.ifEmpty { "Местоположение неизвестно" }

            val isCurrent = session.deviceId == currentDeviceId
            if (isCurrent) {
                binding.chipCurrent.visibility = View.VISIBLE
                binding.buttonRemove.visibility = View.GONE
            } else {
                binding.chipCurrent.visibility = View.GONE
                binding.buttonRemove.visibility = View.VISIBLE
                binding.buttonRemove.setOnClickListener {
                    onRemoveClick(session)
                }
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
