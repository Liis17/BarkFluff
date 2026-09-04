package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import android.widget.CheckedTextView
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import com.barkfluff.client.grpc.GrpcTransportFacade

class GroupMemberPickerAdapter(
    private val onToggle: (GrpcTransportFacade.UserData) -> Unit
) : RecyclerView.Adapter<GroupMemberPickerAdapter.ViewHolder>() {

    private var users: List<GrpcTransportFacade.UserData> = emptyList()
    private var selectedIds: Set<Long> = emptySet()

    fun submit(users: List<GrpcTransportFacade.UserData>, selectedIds: Set<Long>) {
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
        fun bind(user: GrpcTransportFacade.UserData) {
            val name = "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
            view.text = if (user.username.isBlank()) name else view.context.getString(
                R.string.group_member_with_username,
                name,
                user.username
            )
            view.isChecked = user.userId in selectedIds
            view.setOnClickListener { onToggle(user) }
        }
    }
}
