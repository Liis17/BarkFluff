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
import com.barkfluff.client.utils.AvatarLoader
import com.google.android.material.color.DynamicColors
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking

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
    
    // Кэш для URL аватаров: fileId -> URL
    private val avatarUrlCache = mutableMapOf<String, String>()
    
    // Ожидающие запросы для fileId: fileId -> Deferred<String?>
    private val pendingUrlRequests = mutableMapOf<String, Deferred<String?>>()

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
        grpcManager = GrpcManager()

        setupToolbar()
        setupSearchField()
        setupResultsList()

        initGrpcClients()
    }

    private fun initGrpcClients() {
        Log.d(TAG, "initGrpcClients: users=${globalParam.socketUsers}, files=${globalParam.socketFiles}")
        Log.d(TAG, "initGrpcClients: accessToken exists=${globalParam.accessToken != null}, refreshToken exists=${globalParam.refreshToken != null}")
        if (globalParam.socketUsers.isNotBlank()) {
            grpcManager.createUsersClient(globalParam.socketUsers, this, includeDeviceInfo = true)
        }
        if (globalParam.socketFiles.isNotBlank()) {
            grpcManager.createFilesClient(globalParam.socketFiles, this, includeDeviceInfo = true)
        }
        Log.d(TAG, "initGrpcClients: Clients initialized, usersClient is null: ${grpcManager.usersClient == null}, filesClient is null: ${grpcManager.filesClient == null}")
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
                onUserClicked(userData)
            },
            onActionClick = { userData ->
                onActionClicked(userData)
            },
            getFileUrlCallback = { fileId ->
                // Проверяем кэш сначала
                val cachedUrl = avatarUrlCache[fileId]
                if (cachedUrl != null) {
                    Log.d(TAG, "setupResultsList: URL из кэша для fileId=$fileId, url=$cachedUrl")
                    return@UserAdapter cachedUrl
                }
                
                // Проверяем, есть ли уже ожидающий запрос для этого fileId
                val pendingRequest = pendingUrlRequests[fileId]
                if (pendingRequest != null && !pendingRequest.isCompleted) {
                    Log.d(TAG, "setupResultsList: Ожидание существующего запроса для fileId=$fileId")
                    runBlocking {
                        try {
                            pendingRequest.await()
                        } catch (e: Exception) {
                            Log.e(TAG, "setupResultsList: Ошибка ожидания для fileId=$fileId", e)
                            null
                        }
                    }
                }
                
                Log.d(TAG, "setupResultsList: Requesting URL for fileId=$fileId")
                // Создаём новый запрос в IO контексте
                val scope = lifecycleScope
                val deferred = scope.async(Dispatchers.IO) {
                    try {
                        val result = grpcManager.getFileDownloadUrl(fileId)
                        if (result.isSuccess) {
                            val url = result.getOrNull()
                            Log.d(TAG, "setupResultsList: Got URL for fileId=$fileId, url=$url")
                            // Сохраняем в кэш
                            if (url != null) {
                                avatarUrlCache[fileId] = url
                            }
                            url
                        } else {
                            Log.e(TAG, "setupResultsList: Failed to get URL for fileId=$fileId, error=${result.exceptionOrNull()?.message}")
                            null
                        }
                    } finally {
                        // Удаляем из pending запросов
                        pendingUrlRequests.remove(fileId)
                    }
                }
                pendingUrlRequests[fileId] = deferred
                
                runBlocking {
                    try {
                        deferred.await()
                    } catch (e: Exception) {
                        Log.e(TAG, "setupResultsList: Ошибка ожидания для fileId=$fileId", e)
                        null
                    }
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
                    // Используем URL напрямую (profilePicture/profilePicturePreview — это ссылки, не file ID)
                    val avatarUrl = user.profilePicturePreviewUrl.ifBlank { user.profilePictureUrl }
                    UserAdapter.UserDisplayItem(
                        userData = user,
                        displayAvatarFileId = avatarUrl.ifBlank { null },
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

    private fun onUserClicked(userData: GrpcManager.UserData) {
        // TODO: Открытие профиля пользователя или начало чата
        Log.d(TAG, "onUserClicked: userId=${userData.userId}, username=${userData.username}")
    }

    private fun onActionClicked(userData: GrpcManager.UserData) {
        // Кнопка "Написать" - создание или открытие чата с пользователем
        lifecycleScope.launch {
            val result = grpcManager.getPersonChatId(userData.userId)
            if (result.isSuccess) {
                val chatId = result.getOrNull()
                Log.d(TAG, "onActionClicked: ChatId=$chatId")
                // TODO: Открытие чата
            } else {
                Log.e(TAG, "Ошибка получения chatId", result.exceptionOrNull())
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
        grpcManager.shutdown()
    }
}
