package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemCallHistoryBinding

/**
 * Список истории звонков. Каждая строка — завершённый/пропущенный звонок.
 * Резолв имён выполняется во фрагменте, адаптер отображает готовые [Row].
 */
class CallHistoryAdapter(
    private val onRowClick: (Row) -> Unit,
    private val onCallClick: (Row) -> Unit
) : ListAdapter<CallHistoryAdapter.Row, CallHistoryAdapter.ViewHolder>(DIFF) {

    data class Row(
        val callId: String,
        val chatId: String,
        val isGroup: Boolean,
        val peerUserId: Long,
        val title: String,
        val subtitle: String,
        val isMissed: Boolean,
        val isVideo: Boolean
    )

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemCallHistoryBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    inner class ViewHolder(private val binding: ItemCallHistoryBinding) :
        RecyclerView.ViewHolder(binding.root) {

        fun bind(row: Row) {
            binding.callTitle.text = row.title
            binding.callSubtitle.text = row.subtitle

            val tintAttr = if (row.isMissed) {
                android.R.attr.colorError
            } else {
                androidx.appcompat.R.attr.colorPrimary
            }
            val tint = resolveColor(tintAttr)
            binding.directionIcon.setColorFilter(tint)
            binding.callTitle.setTextColor(
                if (row.isMissed) tint else resolveColor(com.google.android.material.R.attr.colorOnSurface)
            )

            binding.callActionButton.setImageResource(
                if (row.isVideo) R.drawable.ic_video else R.drawable.ic_phone
            )

            binding.root.setOnClickListener { onRowClick(row) }
            binding.callActionButton.setOnClickListener { onCallClick(row) }
        }

        private fun resolveColor(attr: Int): Int {
            val typedValue = android.util.TypedValue()
            binding.root.context.theme.resolveAttribute(attr, typedValue, true)
            return typedValue.data
        }
    }

    companion object {
        private val DIFF = object : DiffUtil.ItemCallback<Row>() {
            override fun areItemsTheSame(oldItem: Row, newItem: Row) = oldItem.callId == newItem.callId
            override fun areContentsTheSame(oldItem: Row, newItem: Row) = oldItem == newItem
        }
    }
}
