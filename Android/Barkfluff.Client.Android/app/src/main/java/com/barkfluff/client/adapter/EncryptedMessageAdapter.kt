package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.ItemEncryptedMessageBinding
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Адаптер для отображения расшифрованных сообщений приватных и секретных чатов.
 * Универсальный — работает с любыми (sender, text, sentAt) тройками.
 */
class EncryptedMessageAdapter : ListAdapter<EncryptedMessageItem, EncryptedMessageAdapter.VH>(DIFF) {

    private val timeFormatter = SimpleDateFormat("HH:mm", Locale.getDefault())

    class VH(val binding: ItemEncryptedMessageBinding) : RecyclerView.ViewHolder(binding.root)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val binding = ItemEncryptedMessageBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return VH(binding)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val item = getItem(position)
        with(holder.binding) {
            if (item.senderLabel.isNullOrBlank()) {
                senderTextView.visibility = android.view.View.GONE
            } else {
                senderTextView.visibility = android.view.View.VISIBLE
                senderTextView.text = item.senderLabel
            }
            textTextView.text = item.plaintext ?: "[не удалось расшифровать]"
            timeTextView.text = if (item.sentAtMillis > 0) timeFormatter.format(Date(item.sentAtMillis)) else ""
        }
    }

    companion object {
        val DIFF = object : DiffUtil.ItemCallback<EncryptedMessageItem>() {
            override fun areItemsTheSame(oldItem: EncryptedMessageItem, newItem: EncryptedMessageItem): Boolean =
                oldItem.id == newItem.id
            override fun areContentsTheSame(oldItem: EncryptedMessageItem, newItem: EncryptedMessageItem): Boolean =
                oldItem == newItem
        }
    }
}

data class EncryptedMessageItem(
    val id: String,
    val senderLabel: String?,
    val plaintext: String?,
    val sentAtMillis: Long
)
