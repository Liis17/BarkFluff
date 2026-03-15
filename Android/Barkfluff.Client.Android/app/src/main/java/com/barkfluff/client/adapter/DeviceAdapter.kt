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
            // Если есть кастомное имя - показываем его, иначе только оригинальное
            if (session.customName.isNotEmpty()) {
                binding.textDeviceName.text = session.customName
                binding.textDeviceOriginalName.text = session.originalName
                binding.textDeviceOriginalName.visibility = android.view.View.VISIBLE
                // Показываем appName без версии (первые 2 слова)
                val appNameWithoutVersion = extractAppNameWithoutVersion(session.appName)
                binding.textAppVersion.text = appNameWithoutVersion
                binding.textAppVersion.visibility = android.view.View.VISIBLE
            } else {
                binding.textDeviceName.text = session.originalName
                binding.textDeviceOriginalName.visibility = android.view.View.GONE
                binding.textAppVersion.visibility = android.view.View.GONE
            }

            // Выбираем иконку в зависимости от типа устройства
            binding.imageDeviceIcon.setImageResource(getDeviceIcon(session))

            binding.root.setOnClickListener {
                onItemClick(session)
            }
        }

        /**
         * Извлекает имя приложения без версии (первые 2 слова)
         * Например: "BarkFluff Desktop 1.0.0" -> "BarkFluff Desktop"
         */
        private fun extractAppNameWithoutVersion(appName: String): String {
            val words = appName.split(" ")
            return if (words.size >= 2) {
                // Проверяем, не является ли последнее слово версией (содержит цифры и точки)
                val lastWord = words.last()
                if (lastWord.matches(Regex("^[0-9.]+$"))) {
                    // Последнее слово - версия, убираем его
                    words.dropLast(1).joinToString(" ")
                } else {
                    // Последнее слово не версия, берем первые 2 слова
                    words.take(2).joinToString(" ")
                }
            } else {
                appName
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
