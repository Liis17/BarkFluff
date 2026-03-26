package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import barkfluff.files.FilesApiOuterClass.StickerPackInfo
import com.barkfluff.client.R
import com.google.android.material.card.MaterialCardView

class StickerPackTabAdapter(
    private val onPackSelected: (StickerPackInfo) -> Unit
) : RecyclerView.Adapter<StickerPackTabAdapter.PackViewHolder>() {

    private var packs: List<StickerPackInfo> = emptyList()
    private var selectedPosition: Int = 0

    fun setPacks(list: List<StickerPackInfo>) {
        packs = list
        selectedPosition = if (list.isNotEmpty()) 0 else -1
        notifyDataSetChanged()
    }

    fun setSelected(position: Int) {
        val old = selectedPosition
        selectedPosition = position
        if (old in packs.indices) notifyItemChanged(old)
        if (position in packs.indices) notifyItemChanged(position)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): PackViewHolder {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_sticker_pack_tab, parent, false)
        return PackViewHolder(view as ViewGroup)
    }

    override fun onBindViewHolder(holder: PackViewHolder, position: Int) {
        holder.bind(packs[position], position == selectedPosition)
    }

    override fun getItemCount(): Int = packs.size

    inner class PackViewHolder(itemView: ViewGroup) : RecyclerView.ViewHolder(itemView) {
        private val card: MaterialCardView = itemView.findViewById(R.id.packCard)
        private val nameText: TextView = itemView.findViewById(R.id.packNameText)

        fun bind(pack: StickerPackInfo, isSelected: Boolean) {
            val emoji = pack.name.firstOrNull()?.toString() ?: pack.name.take(2)
            nameText.text = emoji

            card.strokeWidth = if (isSelected) {
                (2 * itemView.resources.displayMetrics.density).toInt()
            } else {
                0
            }
            if (isSelected) {
                val typedValue = android.util.TypedValue()
                itemView.context.theme.resolveAttribute(android.R.attr.colorPrimary, typedValue, true)
                card.setStrokeColor(android.content.res.ColorStateList.valueOf(typedValue.data))
            } else {
                card.setStrokeColor(android.content.res.ColorStateList.valueOf(0))
            }

            itemView.setOnClickListener {
                val pos = bindingAdapterPosition
                if (pos != RecyclerView.NO_POSITION) {
                    setSelected(pos)
                    onPackSelected(packs[pos])
                }
            }
        }
    }
}
