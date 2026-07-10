package com.barkfluff.client

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.widget.doAfterTextChanged
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import barkfluff.files.FilesApiOuterClass
import com.barkfluff.client.adapter.GroupMemberPickerAdapter
import com.barkfluff.client.databinding.ActivityCreateGroupChatBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.repository.ChatRepository
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class CreateGroupChatActivity : AppCompatActivity() {

    private lateinit var binding: ActivityCreateGroupChatBinding
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatRepository: ChatRepository
    private lateinit var adapter: GroupMemberPickerAdapter
    private val selectedUsers = linkedMapOf<Long, GrpcManager.UserData>()
    private var searchResults: List<GrpcManager.UserData> = emptyList()
    private var searchJob: Job? = null
    private var avatarUri: Uri? = null

    private val pickAvatar = registerForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        avatarUri = uri
        binding.avatarButton.text = if (uri == null) getString(R.string.create_group_avatar) else getString(R.string.create_group_avatar_selected)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityCreateGroupChatBinding.inflate(layoutInflater)
        setContentView(binding.root)

        grpcManager = (application as BarkFluffApplication).grpcManager
        chatRepository = ChatRepository(this, grpcManager)
        adapter = GroupMemberPickerAdapter(::toggleUser)
        binding.usersRecyclerView.layoutManager = LinearLayoutManager(this)
        binding.usersRecyclerView.adapter = adapter
        binding.toolbar.setNavigationOnClickListener { finish() }
        binding.avatarButton.setOnClickListener { pickAvatar.launch("image/*") }
        binding.createButton.setOnClickListener { createGroup() }
        binding.searchEditText.doAfterTextChanged { query -> scheduleSearch(query?.toString().orEmpty()) }
    }

    private fun scheduleSearch(rawQuery: String) {
        searchJob?.cancel()
        val query = rawQuery.trim()
        if (query.length < 3) {
            searchResults = emptyList()
            adapter.submit(searchResults, selectedUsers.keys)
            return
        }
        searchJob = lifecycleScope.launch {
            delay(300)
            binding.loadingIndicator.visibility = View.VISIBLE
            val users = grpcManager.searchUsers(query).getOrDefault(emptyList())
            searchResults = users.filter { it.userId != com.barkfluff.client.data.GlobalParam(this@CreateGroupChatActivity).userId }
            adapter.submit(searchResults, selectedUsers.keys)
            binding.loadingIndicator.visibility = View.GONE
        }
    }

    private fun toggleUser(user: GrpcManager.UserData) {
        if (selectedUsers.remove(user.userId) == null) selectedUsers[user.userId] = user
        binding.selectedCount.text = getString(R.string.create_group_members_count, selectedUsers.size)
        adapter.submit(searchResults, selectedUsers.keys)
    }

    private fun createGroup() {
        val title = binding.titleEditText.text?.toString()?.trim().orEmpty()
        if (title.isEmpty()) {
            binding.titleLayout.error = getString(R.string.create_group_name_required)
            return
        }
        if (selectedUsers.isEmpty()) {
            Toast.makeText(this, R.string.create_group_members_required, Toast.LENGTH_SHORT).show()
            return
        }
        lifecycleScope.launch {
            binding.createButton.isEnabled = false
            binding.loadingIndicator.visibility = View.VISIBLE
            val pictureId = avatarUri?.let { uploadAvatar(it) }
            if (avatarUri != null && pictureId == null) {
                binding.createButton.isEnabled = true
                binding.loadingIndicator.visibility = View.GONE
                return@launch
            }
            val result = grpcManager.createGroupChat(selectedUsers.keys.toList(), title, pictureId)
            result.onSuccess { chat ->
                startActivity(Intent(this@CreateGroupChatActivity, ChatActivity::class.java).apply {
                    putExtra("chat_id", chat.id)
                    putExtra("chat_title", chat.title)
                    putExtra("is_group_chat", true)
                })
                finish()
            }.onFailure {
                Toast.makeText(this@CreateGroupChatActivity, getString(R.string.create_group_failed, it.message.orEmpty()), Toast.LENGTH_LONG).show()
            }
            binding.createButton.isEnabled = true
            binding.loadingIndicator.visibility = View.GONE
        }
    }

    private suspend fun uploadAvatar(uri: Uri): String? {
        val bytes = withContext(Dispatchers.IO) { contentResolver.openInputStream(uri)?.use { it.readBytes() } }
        if (bytes == null) return null
        val result = chatRepository.uploadFile(bytes, FilesApiOuterClass.UploadFileType.CHAT_PICTURE)
        if (result.isFailure) Toast.makeText(this, R.string.create_group_avatar_failed, Toast.LENGTH_LONG).show()
        return result.getOrNull()
    }

    override fun onDestroy() {
        searchJob?.cancel()
        chatRepository.close()
        super.onDestroy()
    }
}
