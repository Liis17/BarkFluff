package com.barkfluff.client

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.text.Editable
import android.text.TextWatcher
import android.view.View
import android.view.inputmethod.EditorInfo
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.UserAdapter
import com.barkfluff.client.databinding.ActivitySearchBinding
import com.barkfluff.client.grpc.GrpcManager
import com.google.android.material.color.DynamicColors
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Экран поиска и добавления пользователя в групповой чат.
 * Переиспользует разметку и адаптер экрана поиска.
 */
class AddGroupMemberActivity : AppCompatActivity() {

    private lateinit var binding: ActivitySearchBinding
    private lateinit var grpcManager: GrpcManager
    private lateinit var userAdapter: UserAdapter

    private var chatId: String = ""
    private var searchJob: Job? = null

    companion object {
        private const val TAG = "AddGroupMemberActivity"
        private const val SEARCH_DELAY_MS = 300L
        private const val MIN_SEARCH_LENGTH = 3
        private const val EXTRA_CHAT_ID = "chat_id"

        fun createIntent(context: Context, chatId: String): Intent {
            return Intent(context, AddGroupMemberActivity::class.java).apply {
                putExtra(EXTRA_CHAT_ID, chatId)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivitySearchBinding.inflate(layoutInflater)
        setContentView(binding.root)

        grpcManager = (application as BarkFluffApplication).grpcManager
        chatId = intent.getStringExtra(EXTRA_CHAT_ID) ?: run { finish(); return }

        binding.toolbar.setNavigationOnClickListener { onBackPressedDispatcher.onBackPressed() }
        setupSearchField()
        setupResultsList()
    }

    private fun setupSearchField() {
        binding.searchEditText.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: Editable?) {
                searchJob?.cancel()
                searchJob = lifecycleScope.launch {
                    delay(SEARCH_DELAY_MS)
                    val query = s?.toString()?.trim().orEmpty()
                    if (query.length >= MIN_SEARCH_LENGTH) {
                        searchUsers(query)
                    } else {
                        showHintState()
                    }
                }
            }
        })

        binding.searchEditText.setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_SEARCH) {
                val query = binding.searchEditText.text.toString().trim()
                if (query.length >= MIN_SEARCH_LENGTH) searchUsers(query)
                true
            } else false
        }
    }

    private fun setupResultsList() {
        userAdapter = UserAdapter(
            onUserClick = { userData -> addMember(userData) },
            getFileUrlCallback = { fileId -> grpcManager.getFileDownloadUrl(fileId).getOrNull() }
        )
        binding.searchResultsRecyclerView.apply {
            layoutManager = LinearLayoutManager(this@AddGroupMemberActivity)
            adapter = userAdapter
            setHasFixedSize(false)
        }
    }

    private fun searchUsers(query: String) {
        lifecycleScope.launch {
            showLoading(true)
            val result = grpcManager.searchUsers(query)
            if (result.isSuccess) {
                val users = result.getOrNull() ?: emptyList()
                val displayItems = users.map { user ->
                    val avatarFileId = user.profilePicturePreviewFileId.ifBlank { user.profilePictureFileId }
                    UserAdapter.UserDisplayItem(
                        userData = user,
                        displayAvatarFileId = avatarFileId.ifBlank { null },
                        displayFullName = "${user.firstName} ${user.lastName}".trim().ifBlank { user.username },
                        displayUsername = if (user.username.isNotBlank()) "@${user.username}" else ""
                    )
                }
                userAdapter.submitList(displayItems)
                showEmptyState(displayItems.isEmpty())
            } else {
                showEmptyState(true)
            }
            showLoading(false)
        }
    }

    private fun addMember(userData: GrpcManager.UserData) {
        lifecycleScope.launch {
            showLoading(true)
            val result = grpcManager.addUser(chatId, userData.userId)
            showLoading(false)
            if (result.isSuccess) {
                Toast.makeText(this@AddGroupMemberActivity, "Участник добавлен", Toast.LENGTH_SHORT).show()
                setResult(RESULT_OK)
                finish()
            } else {
                Toast.makeText(this@AddGroupMemberActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun showLoading(show: Boolean) {
        binding.loadingIndicator.visibility = if (show) View.VISIBLE else View.GONE
    }

    private fun showEmptyState(show: Boolean) {
        binding.emptyState.visibility = if (show) View.VISIBLE else View.GONE
        binding.searchResultsRecyclerView.visibility = if (show) View.GONE else View.VISIBLE
        binding.hintState.visibility = View.GONE
    }

    private fun showHintState() {
        binding.hintState.visibility = View.VISIBLE
        binding.emptyState.visibility = View.GONE
        binding.searchResultsRecyclerView.visibility = View.GONE
        binding.loadingIndicator.visibility = View.GONE
    }

    override fun onDestroy() {
        super.onDestroy()
        searchJob?.cancel()
    }
}
