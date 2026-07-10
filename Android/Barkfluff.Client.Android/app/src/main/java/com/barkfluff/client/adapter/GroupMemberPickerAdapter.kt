package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import android.widget.CheckedTextView
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import com.barkfluff.client.grpc.GrpcManager

class GroupMemberPickerAdapter(
    private val onToggle: (GrpcManager.UserData) -> Unit
) : RecyclerView.Adapter<GroupMemberPickerAdapter.ViewHolder>() {

    private var users: List<GrpcManager.UserData> = emptyList()
    private var selectedIds: Set<Long> = emptySet()

    fun submit(users: List<GrpcManager.UserData>, selectedIds: Set<Long>) {
        this.users = users
        this.selectedIds = selectedIds
        notifyDataSetChanged()
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder = ViewHolder(
        LayoutInflater.from(parent.context).inflate(R.layout.item_group_member_picker, parent, false) as CheckedTextView
    )

    override fun onBindViewHolder(holder: ViewHolder, position: Int) = holder.bind(users[position])

    override fun getItemCount(): Int = users.size

    inner class ViewHolder(private val view: CheckedTextView) : RecyclerView.ViewHolder(view) {
        fun bind(user: GrpcManager.UserData) {
            val name = "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
            view.text = if (user.username.isBlank()) name else "$name · @${user.username}"
            view.isChecked = user.userId in selectedIds
            view.setOnClickListener { onToggle(user) }
        }
    }
}
