package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R

class FolderTabsAdapter(
    private val onSelect: (folderId: String?) -> Unit
) : RecyclerView.Adapter<FolderTabsAdapter.VH>() {

    data class Item(
        val id: String?,
        val icon: String,
        val name: String,
        val unreadCount: Int
    )

    private val items = mutableListOf<Item>()
    private var compactMode: Boolean = false
    private var selectedId: String? = null

    fun submit(newItems: List<Item>, compact: Boolean, selected: String?) {
        items.clear()
        items.addAll(newItems)
        compactMode = compact
        selectedId = selected
        notifyDataSetChanged()
    }

    fun updateSelection(selected: String?) {
        selectedId = selected
        notifyDataSetChanged()
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_folder_tab, parent, false)
        return VH(view)
    }

    override fun getItemCount(): Int = items.size

    override fun onBindViewHolder(holder: VH, position: Int) {
        val item = items[position]
        val displayIcon = if (item.icon.isBlank()) DEFAULT_ICON else item.icon
        holder.icon.text = displayIcon

        holder.name.text = item.name
        holder.name.visibility = if (compactMode) View.GONE else View.VISIBLE

        if (item.unreadCount > 0) {
            holder.badge.visibility = View.VISIBLE
            holder.badge.text = if (item.unreadCount > 99) "99+" else item.unreadCount.toString()
        } else {
            holder.badge.visibility = View.GONE
        }

        holder.root.isSelected = item.id == selectedId
        holder.root.setOnClickListener {
            if (item.id != selectedId) {
                onSelect(item.id)
            }
        }
    }

    class VH(itemView: View) : RecyclerView.ViewHolder(itemView) {
        val root: View = itemView.findViewById(R.id.folderTabRoot)
        val icon: TextView = itemView.findViewById(R.id.folderIcon)
        val name: TextView = itemView.findViewById(R.id.folderName)
        val badge: TextView = itemView.findViewById(R.id.folderUnreadBadge)
    }

    companion object {
        private const val DEFAULT_ICON = "📋"
    }
}
