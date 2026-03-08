package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
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

            // Выбираем иконку в зависимости от типа устройства
            binding.imageDeviceIcon.setImageResource(getDeviceIcon(session))

            binding.root.setOnClickListener {
                onItemClick(session)
            }
        }
    }

    /**
     * Возвращает ID ресурса иконки в зависимости от типа устройства.
     */
    private fun getDeviceIcon(session: GrpcManager.SessionData): Int {
        val os = session.os.lowercase()
        val originalName = session.originalName.lowercase()
        val customName = session.customName.lowercase()
        val combinedName = "$originalName $customName"

        return when {
            // Steam Deck (проверяем первым, так как специфичнее)
            combinedName.contains("steam deck") || combinedName.contains("steamdeck") -> R.drawable.ic_steam_deck
            os.contains("steamos") || os.contains("steam os") -> R.drawable.ic_steam_deck

            // Tablet / iPad
            os.contains("ipad") || os.contains("tablet") -> R.drawable.ic_tablet
            combinedName.contains("tablet") || combinedName.contains("ipad") || combinedName.contains("планшет") -> R.drawable.ic_tablet

            // Desktop / PC (проверяем имя и ОС)
            isDesktop(os, combinedName) -> R.drawable.ic_desktop

            // Smartphone (по умолчанию для Android/iOS)
            else -> R.drawable.ic_smartphone
        }
    }

    private fun isDesktop(os: String, name: String): Boolean {
        // Проверка по ОС
        val desktopOs = os.contains("windows") && !os.contains("phone") ||
                os.contains("macos") || os.contains("mac os") ||
                os.contains("linux") && !os.contains("android") ||
                os.contains("ubuntu") || os.contains("fedora") || os.contains("debian")

        // Проверка по имени устройства
        val desktopName = name.contains("desktop") || name.contains(" pc") || name.contains("pc ") ||
                name.contains("computer") || name.contains("windows") && !name.contains("phone") ||
                name.contains("macbook") || name.contains("imac") || name.contains("macmini")

        return desktopOs || desktopName
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
