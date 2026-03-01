package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.text.Editable
import android.text.TextWatcher
import android.util.Log
import android.view.View
import android.view.inputmethod.EditorInfo
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.UserAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivitySearchBinding
import com.barkfluff.client.grpc.GrpcManager
import com.google.android.material.color.DynamicColors
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Экран поиска пользователей
 * Поиск через UsersApi.SearchUsers с задержкой 300мс после последнего ввода
 * Поиск начинается от 3 символов
 */
class SearchActivity : AppCompatActivity() {

    private lateinit var binding: ActivitySearchBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var userAdapter: UserAdapter

    private var searchJob: Job? = null

    companion object {
        private const val TAG = "SearchActivity"
        private const val SEARCH_DELAY_MS = 300L
        private const val MIN_SEARCH_LENGTH = 3
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivitySearchBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)
        grpcManager = (application as BarkFluffApplication).grpcManager

        setupToolbar()
        setupSearchField()
        setupResultsList()
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }
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

        // Поиск по нажатию Enter/IMEE_ACTION
        binding.searchEditText.setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_SEARCH) {
                val query = binding.searchEditText.text.toString().trim()
                if (query.length >= MIN_SEARCH_LENGTH) {
                    searchUsers(query)
                }
                true
            } else {
                false
            }
        }
    }

    private fun setupResultsList() {
        userAdapter = UserAdapter(
            onUserClick = { userData ->
                openChatWithUser(userData)
            },
            getFileUrlCallback = { fileId ->
                Log.d(TAG, "setupResultsList: Requesting URL for fileId=$fileId")
                val result = grpcManager.getFileDownloadUrl(fileId)
                if (result.isSuccess) {
                    val url = result.getOrNull()
                    Log.d(TAG, "setupResultsList: Got URL for fileId=$fileId, url=$url")
                    url
                } else {
                    Log.e(TAG, "setupResultsList: Failed to get URL for fileId=$fileId, error=${result.exceptionOrNull()?.message}")
                    null
                }
            }
        )

        binding.searchResultsRecyclerView.apply {
            layoutManager = LinearLayoutManager(this@SearchActivity)
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
                Log.d(TAG, "Найдено ${users.size} пользователей по запросу '$query'")

                val displayItems = users.map { user ->
                    // Используем извлечённый fileId (GUID), а не сырой URL Minio
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
                Log.e(TAG, "Ошибка поиска пользователей", result.exceptionOrNull())
                showEmptyState(true)
            }

            showLoading(false)
        }
    }

    private fun openChatWithUser(userData: GrpcManager.UserData) {
        Log.d(TAG, "openChatWithUser: userId=${userData.userId}, username=${userData.username}")

        lifecycleScope.launch {
            showLoading(true)

            val result = grpcManager.getPersonChatId(userData.userId)
            if (result.isSuccess) {
                val chatId = result.getOrNull()
                Log.d(TAG, "openChatWithUser: Got chatId=$chatId")

                // Формируем отображаемое имя
                val displayName = "${userData.firstName} ${userData.lastName}".trim().ifBlank { userData.username }
                // Получаем fileId аватара
                val avatarFileId = userData.profilePicturePreviewFileId.ifBlank { userData.profilePictureFileId }.ifBlank { null }

                // Открываем ChatActivity
                val intent = Intent(this@SearchActivity, ChatActivity::class.java).apply {
                    putExtra("chat_id", chatId)
                    putExtra("chat_title", displayName)
                    putExtra("chat_avatar_file_id", avatarFileId)
                    putExtra("is_group_chat", false)
                    putExtra("other_user_id", userData.userId)
                }
                startActivity(intent)
            } else {
                Log.e(TAG, "Ошибка получения chatId", result.exceptionOrNull())
                android.widget.Toast.makeText(
                    this@SearchActivity,
                    "Не удалось открыть чат: ${result.exceptionOrNull()?.message}",
                    android.widget.Toast.LENGTH_SHORT
                ).show()
            }

            showLoading(false)
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
