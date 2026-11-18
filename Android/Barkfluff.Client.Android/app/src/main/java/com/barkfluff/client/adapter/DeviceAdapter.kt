package com.barkfluff.client.adapter

import android.content.Context
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemDeviceBinding
import com.barkfluff.client.domain.model.SessionData

class DeviceAdapter(
    private val onItemClick: (SessionData) -> Unit
) : ListAdapter<SessionData, DeviceAdapter.DeviceViewHolder>(DeviceDiffCallback()) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): DeviceViewHolder {
        val binding = ItemDeviceBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return DeviceViewHolder(binding)
    }

    override fun onBindViewHolder(holder: DeviceViewHolder, position: Int) {
        holder.bind(getItem(position), position)
    }

    override fun onCurrentListChanged(previousList: List<SessionData>, currentList: List<SessionData>) {
        // Скругления группы зависят от первой и последней строки: при изменении размера
        // соседние строки, которые DiffUtil не перепривязал, получают новый фон.
        if (previousList.size != currentList.size && currentList.isNotEmpty()) {
            notifyItemRangeChanged(0, currentList.size, PAYLOAD_GROUP_SHAPE)
        }
    }

    override fun onBindViewHolder(holder: DeviceViewHolder, position: Int, payloads: MutableList<Any>) {
        if (payloads.isNotEmpty() && payloads.all { it == PAYLOAD_GROUP_SHAPE }) {
            holder.bindGroupShape(position)
        } else {
            onBindViewHolder(holder, position)
        }
    }

    inner class DeviceViewHolder(
        private val binding: ItemDeviceBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(session: SessionData, position: Int) {
            val context = binding.root.context
            binding.textDeviceName.text = displayName(context, session)
            val details = details(session)
            binding.textDeviceDetails.text = details
            binding.textDeviceDetails.visibility = if (details.isEmpty()) View.GONE else View.VISIBLE
            binding.imageDeviceIcon.setImageResource(getDeviceIcon(session))
            bindGroupShape(position)

            binding.root.setOnClickListener {
                onItemClick(session)
            }
        }

        fun bindGroupShape(position: Int) {
            binding.root.setBackgroundResource(groupBackground(position, itemCount))
        }
    }

    companion object {
        private const val PAYLOAD_GROUP_SHAPE = "group_shape"

        /** Фон строки в группе настроек по её позиции. */
        fun groupBackground(position: Int, count: Int): Int = when {
            count <= 1 -> R.drawable.bg_settings_item_single
            position == 0 -> R.drawable.bg_settings_item_top
            position == count - 1 -> R.drawable.bg_settings_item_bottom
            else -> R.drawable.bg_settings_item_middle
        }

        /** Заголовок: пользовательское имя, затем исходное, затем ОС или «Неизвестное устройство». */
        fun displayName(context: Context, session: SessionData): String =
            session.customName.ifBlank { session.originalName }.ifBlank { session.os }
                .ifBlank { context.getString(R.string.device_unknown_name) }

        /** Подпись: исходное имя (если заголовок переименован) и приложение без версии. */
        fun details(session: SessionData): String = listOfNotNull(
            session.originalName.takeIf { session.customName.isNotBlank() && it.isNotBlank() },
            appNameWithoutVersion(session.appName).takeIf { it.isNotBlank() },
        ).joinToString(" · ")

        /**
         * Извлекает имя приложения без версии (первые 2 слова)
         * Например: "BarkFluff Desktop 1.0.0" -> "BarkFluff Desktop"
         */
        fun appNameWithoutVersion(appName: String): String {
            val words = appName.trim().split(" ")
            return if (words.size >= 2) {
                // Проверяем, не является ли последнее слово версией (содержит цифры и точки)
                val lastWord = words.last()
                if (lastWord.matches(Regex("^[0-9.]+$"))) {
                    words.dropLast(1).joinToString(" ")
                } else {
                    words.take(2).joinToString(" ")
                }
            } else {
                appName.trim()
            }
        }

        /**
         * Возвращает ID ресурса иконки в зависимости от типа устройства.
         * Доступен извне для использования в DevicesActivity (текущее устройство).
         */
        fun getDeviceIcon(session: SessionData): Int {
            val os = session.os.lowercase()
            val originalName = session.originalName.lowercase()
            val customName = session.customName.lowercase()
            val appName = session.appName.lowercase()
            val combinedName = "$originalName $customName"

            return when {
                // Steam Deck
                combinedName.contains("steam deck") || combinedName.contains("steamdeck") -> R.drawable.ic_steam_deck
                os.contains("steamos") || os.contains("steam os") -> R.drawable.ic_steam_deck

                // VR-гарнитура
                isVrDevice(os, combinedName) -> R.drawable.ic_vr_headset

                // Google Pixel
                originalName.contains("pixel") -> R.drawable.ic_pixel

                // Samsung Galaxy Ultra
                isSamsungUltra(originalName) -> R.drawable.ic_samsung_ultra

                // iPhone
                originalName.contains("iphone") -> R.drawable.ic_iphone

                // Tablet / iPad
                os.contains("ipad") || os.contains("tablet") -> R.drawable.ic_tablet
                combinedName.contains("tablet") || combinedName.contains("ipad") || combinedName.contains("планшет") -> R.drawable.ic_tablet

                // Web-клиент
                appName.contains("web") -> R.drawable.ic_web

                // Desktop / PC (ноутбук)
                isDesktop(os, combinedName) -> R.drawable.ic_desktop

                // Smartphone (по умолчанию)
                else -> R.drawable.ic_smartphone
            }
        }

        private fun isVrDevice(os: String, name: String): Boolean {
            val vrKeywords = listOf("quest", "oculus", "pico", "vive", "valve index", "vision pro")
            if (vrKeywords.any { name.contains(it) }) return true
            if (os.contains("vr") || os.contains("xros") || os.contains("quest")) return true
            return false
        }

        private fun isSamsungUltra(name: String): Boolean {
            if (name.contains("sm-s9")) return true
            if (name.contains("galaxy") && name.contains("ultra")) return true
            return false
        }

        private fun isDesktop(os: String, name: String): Boolean {
            val desktopOs = os.contains("windows") && !os.contains("phone") ||
                    os.contains("macos") || os.contains("mac os") ||
                    os.contains("linux") && !os.contains("android") ||
                    os.contains("ubuntu") || os.contains("fedora") || os.contains("debian")

            val desktopName = name.contains("desktop") || name.contains(" pc") || name.contains("pc ") ||
                    name.contains("computer") || name.contains("windows") && !name.contains("phone") ||
                    name.contains("macbook") || name.contains("imac") || name.contains("macmini")

            return desktopOs || desktopName
        }
    }

    class DeviceDiffCallback : DiffUtil.ItemCallback<SessionData>() {
        override fun areItemsTheSame(oldItem: SessionData, newItem: SessionData): Boolean {
            return oldItem.id == newItem.id
        }

        override fun areContentsTheSame(oldItem: SessionData, newItem: SessionData): Boolean {
            return oldItem == newItem
        }
    }
}
