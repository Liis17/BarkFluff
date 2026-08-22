package com.barkfluff.client

import android.animation.ValueAnimator
import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.view.animation.PathInterpolator
import androidx.core.animation.doOnEnd
import androidx.core.view.doOnLayout
import androidx.core.view.updateLayoutParams
import androidx.fragment.app.Fragment
import androidx.core.os.bundleOf
import androidx.fragment.app.viewModels
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import androidx.activity.result.contract.ActivityResultContracts
import com.barkfluff.client.adapter.ChatAdapter
import com.barkfluff.client.adapter.ChatSkeletonAdapter
import com.barkfluff.client.adapter.FolderTabsAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.databinding.FragmentChatsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import com.google.android.material.snackbar.Snackbar
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch

@AndroidEntryPoint
class ChatsFragment : Fragment() {

    private var _binding: FragmentChatsBinding? = null
    private val binding get() = _binding!!

    private val viewModel: ChatsViewModel by viewModels()

    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatAdapter: ChatAdapter
    private lateinit var foldersAdapter: FolderTabsAdapter
    private lateinit var skeletonAdapter: ChatSkeletonAdapter
    private var titleAnimationGeneration = 0

    // Сворачивание шапки по направлению прокрутки
    private var headerCollapsed = false
    private var headerExpandedHeight = 0
    private var headerAnimator: ValueAnimator? = null
    private var searchAnimator: ValueAnimator? = null

    private val foldersSettingsLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { _ ->
        // По возвращении из настроек папок — перезагрузить
        viewModel.loadChats()
    }

    companion object {
        private const val TAG = "ChatsFragment"
        private const val TITLE_FADE_OUT_DURATION_MS = 90L
        private const val TITLE_FADE_IN_DURATION_MS = 160L
        private const val HEADER_COLLAPSE_DURATION_MS = 360L
        private const val SEARCH_RESIZE_DURATION_MS = 300L
        private const val SEARCH_HEIGHT_EXPANDED_DP = 52
        private const val SEARCH_HEIGHT_COLLAPSED_DP = 48
        /** Порог прокрутки, ниже которого шапка не сворачивается (как в макете). */
        private const val HEADER_COLLAPSE_TRIGGER_DP = 20
        /** Минимальный шаг прокрутки, меняющий состояние шапки. */
        private const val HEADER_SCROLL_THRESHOLD_DP = 6
        const val MAIN_UNREAD_RESULT_KEY = "main_chats_unread"
        const val MAIN_UNREAD_COUNT = "count"
    }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentChatsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        val app = requireActivity().application as BarkFluffApplication
        globalParam = GlobalParam(requireContext())
        grpcManager = app.grpcManager

        setupToolbar()
        setupChatList()
        setupFolderTabs()
        setupSearchButton()
        showSkeleton()

        observeViewModel()
        viewModel.initialize()
    }

    /**
     * Рендер состояния ChatsViewModel: список чатов, папки, сабтитул шапки, скелетон/empty-state.
     */
    private fun observeViewModel() {
        viewLifecycleOwner.lifecycleScope.launch {
            var previousState: ChatsUiState? = null
            var previousItems: List<ChatAdapter.ChatDisplayItem> = emptyList()
            viewModel.uiState.collect { state ->
                val prev = previousState

                // Скелетон, пока нет ни кэша, ни серверного списка
                if (!state.contentAvailable) {
                    showSkeleton()
                } else if (state.displayItems != previousItems || prev?.contentAvailable != true) {
                    if (binding.chatRecyclerView.adapter !== chatAdapter) {
                        binding.chatRecyclerView.adapter = chatAdapter
                    }
                    chatAdapter.submitList(state.displayItems)
                    showEmptyState(state.displayItems.isEmpty())
                    previousItems = state.displayItems
                }

                // Папки
                if (prev?.folders != state.folders || prev?.allChats != state.allChats ||
                    prev?.selectedFolderId != state.selectedFolderId
                ) {
                    renderFolderTabs()
                }

                // Сабтитул и счётчик непрочитанных
                if (prev == null ||
                    prev.syncStatus != state.syncStatus ||
                    prev.isRealtimeReconnecting != state.isRealtimeReconnecting ||
                    prev.unreadCount != state.unreadCount
                ) {
                    updateHeaderSubtitle()
                    publishMainUnread(state.unreadCount)
                }

                binding.toolbarRetryButton.visibility =
                    if (state.syncStatus == ChatsSyncStatus.OFFLINE) View.VISIBLE else View.GONE

                previousState = state
            }
        }

        viewLifecycleOwner.lifecycleScope.launch {
            viewModel.events.collect { event ->
                when (event) {
                    ChatsEvent.NavigateToLogin -> navigateToLogin()
                    ChatsEvent.RefreshUserAvatar -> loadUserAvatar()
                    is ChatsEvent.ChatLoadError -> Snackbar.make(
                        binding.root,
                        getString(R.string.chat_load_error, event.message),
                        Snackbar.LENGTH_LONG
                    ).show()
                }
            }
        }
    }

    private fun setupFolderTabs() {
        foldersAdapter = FolderTabsAdapter { folderId ->
            foldersAdapter.updateSelection(folderId)
            viewModel.selectFolder(folderId)
        }
        binding.foldersRecyclerView.apply {
            layoutManager = LinearLayoutManager(requireContext(), LinearLayoutManager.HORIZONTAL, false)
            adapter = foldersAdapter
        }
    }

    override fun onResume() {
        super.onResume()
        // Перерендерить сегмент папок с актуальными настройками компактности
        if (_binding != null && ::foldersAdapter.isInitialized && viewModel.uiState.value.folders.isNotEmpty()) {
            renderFolderTabs()
        }
        val app = requireActivity().application as BarkFluffApplication
        if (app.cameFromBackground) {
            app.cameFromBackground = false
            // Перезагружаем список чатов при возврате из фона,
            // т.к. реалтайм-события в фоне не приходят
            viewModel.reloadFromBackground()
        }
    }

    private fun setupSearchButton() {
        binding.searchField.setOnClickListener {
            val intent = Intent(requireContext(), SearchActivity::class.java)
            startActivity(intent)
        }
    }

    private fun setupToolbar() {
        updateHeaderSubtitle(animate = false)
        binding.headerCollapsible.doOnLayout {
            if (headerExpandedHeight == 0 && !headerCollapsed) headerExpandedHeight = it.height
        }
        binding.toolbarRetryButton.setOnClickListener {
            viewModel.retry()
        }
    }

    /**
     * Вторая строка шапки: статус синхронизации, если он есть, иначе — счётчик непрочитанных.
     */
    private fun updateHeaderSubtitle(animate: Boolean = true) {
        if (_binding == null) return
        val state = viewModel.uiState.value
        val subtitle = when {
            state.isRealtimeReconnecting -> getString(R.string.connecting)
            state.syncStatus == ChatsSyncStatus.UPDATING -> getString(R.string.chats_sync_updating)
            state.syncStatus == ChatsSyncStatus.OFFLINE -> getString(R.string.chats_sync_offline)
            else -> {
                val unread = state.unreadCount
                if (unread == 0) getString(R.string.chats_unread_none)
                else resources.getQuantityString(R.plurals.chats_unread_summary, unread, unread)
            }
        }

        if (binding.headerSubtitle.text == subtitle) return

        binding.headerSubtitle.animate().cancel()
        if (!animate) {
            binding.headerSubtitle.alpha = 1f
            binding.headerSubtitle.translationY = 0f
            binding.headerSubtitle.text = subtitle
            return
        }

        val generation = ++titleAnimationGeneration
        val offset = 4 * resources.displayMetrics.density
        binding.headerSubtitle.animate()
            .alpha(0f)
            .translationY(-offset)
            .setDuration(TITLE_FADE_OUT_DURATION_MS)
            .withEndAction {
                if (_binding == null || generation != titleAnimationGeneration) return@withEndAction
                binding.headerSubtitle.text = subtitle
                binding.headerSubtitle.translationY = offset
                binding.headerSubtitle.animate()
                    .alpha(1f)
                    .translationY(0f)
                    .setDuration(TITLE_FADE_IN_DURATION_MS)
                    .start()
            }
            .start()
    }

    /**
     * Прокрутка вниз сворачивает крупный заголовок и сжимает поле поиска, прокрутка вверх — возвращает.
     */
    private fun updateHeaderCollapse(recyclerView: RecyclerView, dy: Int) {
        val density = resources.displayMetrics.density
        val threshold = HEADER_SCROLL_THRESHOLD_DP * density
        when {
            dy > threshold && recyclerView.computeVerticalScrollOffset() > HEADER_COLLAPSE_TRIGGER_DP * density ->
                setHeaderCollapsed(true)
            dy < -threshold -> setHeaderCollapsed(false)
        }
    }

    private fun setHeaderCollapsed(collapsed: Boolean) {
        if (headerCollapsed == collapsed || _binding == null) return
        val header = binding.headerCollapsible
        if (headerExpandedHeight == 0) {
            headerExpandedHeight = header.height
            if (headerExpandedHeight == 0) return
        }
        headerCollapsed = collapsed

        val target = if (collapsed) 0 else headerExpandedHeight
        headerAnimator?.cancel()
        headerAnimator = ValueAnimator.ofInt(header.height, target).apply {
            duration = HEADER_COLLAPSE_DURATION_MS
            interpolator = PathInterpolator(0.2f, 0f, 0f, 1f)
            addUpdateListener { animator ->
                if (_binding == null) return@addUpdateListener
                val value = animator.animatedValue as Int
                header.updateLayoutParams { height = value }
                header.alpha = value.toFloat() / headerExpandedHeight
            }
            doOnEnd {
                if (_binding == null || collapsed) return@doOnEnd
                header.updateLayoutParams { height = ViewGroup.LayoutParams.WRAP_CONTENT }
            }
            start()
        }

        val searchField = binding.searchField
        val searchTarget = ((if (collapsed) SEARCH_HEIGHT_COLLAPSED_DP else SEARCH_HEIGHT_EXPANDED_DP) *
            resources.displayMetrics.density).toInt()
        searchAnimator?.cancel()
        searchAnimator = ValueAnimator.ofInt(searchField.height, searchTarget).apply {
            duration = SEARCH_RESIZE_DURATION_MS
            interpolator = PathInterpolator(0.2f, 0f, 0f, 1f)
            addUpdateListener { animator ->
                if (_binding == null) return@addUpdateListener
                searchField.updateLayoutParams { height = animator.animatedValue as Int }
            }
            start()
        }
    }

    private fun loadUserAvatar() {
        val fullName = "${globalParam.firstName} ${globalParam.lastName}".trim()
        val avatarFileId = globalParam.pictureFileId
        val avatarPreviewFileId = globalParam.picturePreviewFileId

        Log.d(TAG, "loadUserAvatar: fullName='$fullName', avatarFileId='$avatarFileId', avatarPreviewFileId='$avatarPreviewFileId', userId=${globalParam.userId}")

        // Пробуем загрузить из URL напрямую если он есть
        val urlToUse = globalParam.profilePictureUrl.ifBlank { globalParam.picturePreviewUrl }

        if (urlToUse.isNotBlank()) {
            Log.d(TAG, "loadUserAvatar: Loading from URL")
            // Сначала показываем placeholder пока изображение загружается
            AvatarLoader.showPlaceholder(binding.userAvatarPlaceholder, fullName.ifBlank { globalParam.userName }, globalParam.userId)
            binding.userAvatar.visibility = View.GONE
            
            AvatarLoader.load(
                imageView = binding.userAvatar,
                placeholderView = binding.userAvatarPlaceholder,
                avatarUrl = urlToUse,
                displayName = fullName.ifBlank { globalParam.userName },
                userId = globalParam.userId
            )
            return
        }

        val useFileId = avatarFileId.ifBlank { avatarPreviewFileId }

        if (useFileId.isBlank()) {
            Log.d(TAG, "loadUserAvatar: No fileId, showing placeholder")
            AvatarLoader.showPlaceholder(binding.userAvatarPlaceholder, fullName.ifBlank { globalParam.userName }, globalParam.userId)
            binding.userAvatar.visibility = View.GONE
            return
        }

        // Сначала показываем placeholder пока изображение загружается
        AvatarLoader.showPlaceholder(binding.userAvatarPlaceholder, fullName.ifBlank { globalParam.userName }, globalParam.userId)
        binding.userAvatar.visibility = View.GONE
        
        AvatarLoader.loadByFileId(
            imageView = binding.userAvatar,
            placeholderView = binding.userAvatarPlaceholder,
            fileId = useFileId,
            displayName = fullName.ifBlank { globalParam.userName },
            userId = globalParam.userId,
            size = 64
        ) {
            val result = grpcManager.getFileDownloadUrl(useFileId)
            if (result.isSuccess) result.getOrNull() else null
        }
    }

    private fun setupChatList() {
        chatAdapter = ChatAdapter({ chat ->
            onChatClicked(chat)
        }) { fileId ->
            Log.d(TAG, "setupChatList: Requesting URL for fileId=$fileId")
            val result = grpcManager.getFileDownloadUrl(fileId)
            if (result.isSuccess) {
                val url = result.getOrNull()
                Log.d(TAG, "setupChatList: Got URL for fileId=$fileId")
                url
            } else {
                Log.e(TAG, "setupChatList: Failed to get URL for fileId=$fileId, error=${result.exceptionOrNull()?.message}")
                null
            }
        }

        chatAdapter.currentUserId = globalParam.userId
        skeletonAdapter = ChatSkeletonAdapter()

        binding.chatRecyclerView.apply {
            layoutManager = LinearLayoutManager(requireContext())
            adapter = skeletonAdapter
            setHasFixedSize(false)
            addOnScrollListener(object : RecyclerView.OnScrollListener() {
                override fun onScrolled(recyclerView: RecyclerView, dx: Int, dy: Int) {
                    updateHeaderCollapse(recyclerView, dy)
                    if (dy <= 0 || viewModel.uiState.value.isLoadingNextPage) return
                    val state = viewModel.uiState.value
                    if (state.allChats.size >= state.totalChatsCount) return
                    val layoutManager = recyclerView.layoutManager as? LinearLayoutManager ?: return
                    if (layoutManager.findLastVisibleItemPosition() >= chatAdapter.itemCount - 6) viewModel.loadNextPage()
                }
            })
        }
    }

    private fun renderFolderTabs() {
        val state = viewModel.uiState.value
        if (state.folders.isEmpty()) {
            binding.foldersRecyclerView.visibility = View.GONE
            return
        }
        binding.foldersRecyclerView.visibility = View.VISIBLE

        val allChatsItem = FolderTabsAdapter.Item(
            id = null,
            icon = "",
            name = getString(R.string.all_chats),
            unreadCount = computeAllChatsUnread(state)
        )
        val folderItems = state.folders.map { folder ->
            FolderTabsAdapter.Item(
                id = folder.folderId,
                icon = folder.folderIcon,
                name = folder.folderName,
                unreadCount = computeFolderUnread(state, folder.chatIds)
            )
        }
        foldersAdapter.submit(
            newItems = listOf(allChatsItem) + folderItems,
            compact = globalParam.compactFolders,
            noOutline = globalParam.folderTabsNoOutline,
            selected = state.selectedFolderId
        )
    }

    /** Множество id чатов, входящих хотя бы в одну пользовательскую папку. */
    private fun chatsInUserFolders(state: ChatsUiState): Set<String> {
        if (state.folders.isEmpty()) return emptySet()
        val s = HashSet<String>()
        for (f in state.folders) s.addAll(f.chatIds)
        return s
    }

    private fun computeFolderUnread(state: ChatsUiState, folderChatIds: List<String>): Int {
        if (folderChatIds.isEmpty() || state.allChats.isEmpty()) return 0
        val ids = folderChatIds.toSet()
        var sum = 0
        for (chat in state.allChats) {
            if (chat.id in ids) sum += chat.countUnread.toInt()
        }
        return sum
    }

    private fun computeAllChatsUnread(state: ChatsUiState): Int {
        if (state.allChats.isEmpty()) return 0
        val exclude = globalParam.excludeFolderChatsFromAll
        val inFolders = if (exclude) chatsInUserFolders(state) else emptySet()
        var sum = 0
        for (chat in state.allChats) {
            if (exclude && chat.id in inFolders) continue
            sum += chat.countUnread.toInt()
        }
        return sum
    }

    private fun publishMainUnread(unreadCount: Int) {
        if (!isAdded) return
        updateHeaderSubtitle()
        parentFragmentManager.setFragmentResult(
            MAIN_UNREAD_RESULT_KEY,
            bundleOf(MAIN_UNREAD_COUNT to unreadCount)
        )
    }

    fun scrollToTop() {
        if (_binding != null) binding.chatRecyclerView.smoothScrollToPosition(0)
    }

    private fun onChatClicked(chat: GrpcManager.ChatData) {
        // Находим display item для получения дополнительной информации
        val displayItem = chatAdapter.currentList.find { !it.isFooter && it.chatData.id == chat.id }

        if (chat.chatType == barkfluff.shared.Shared.ChatType.CHAT_TYPE_PRIVATE) {
            startActivity(ChatActivity.privateChatIntent(
                requireContext(),
                chatId = chat.id,
                title = displayItem?.displayTitle ?: chat.title.ifBlank { getString(R.string.create_chat_private) },
                inviteState = chat.privateInviteState.number,
                inviterUserId = chat.privateInviterUserId
            ))
            return
        }

        val intent = Intent(requireContext(), ChatActivity::class.java).apply {
            putExtra("chat_id", chat.id)
            putExtra("chat_title", displayItem?.displayTitle ?: chat.title.ifBlank { getString(R.string.chat_title_default) })
            putExtra("chat_avatar_file_id", displayItem?.displayAvatarFileId ?: chat.pictureFileId.ifBlank { null })
            putExtra("is_group_chat", chat.isGroupChat)
            putExtra("other_user_id", displayItem?.otherUserId ?: 0L)
        }

        // Устанавливаем чат как открытый перед запуском Activity
        OpenChatManager.setOpenChat(chat.id)

        startActivity(intent)
    }

    private fun showSkeleton() {
        binding.loadingIndicator.visibility = View.GONE
        binding.emptyState.visibility = View.GONE
        binding.chatRecyclerView.visibility = View.VISIBLE
        if (::skeletonAdapter.isInitialized && binding.chatRecyclerView.adapter !== skeletonAdapter) {
            binding.chatRecyclerView.adapter = skeletonAdapter
        }
    }

    private fun showEmptyState(show: Boolean) {
        binding.emptyState.visibility = if (show) View.VISIBLE else View.GONE
        binding.chatRecyclerView.visibility = if (show) View.GONE else View.VISIBLE
    }

    private fun navigateToLogin() {
        val intent = Intent(requireContext(), LoginActivity::class.java)
        startActivity(intent)
        requireActivity().finish()
    }

    override fun onDestroyView() {
        super.onDestroyView()
        headerAnimator?.cancel()
        headerAnimator = null
        searchAnimator?.cancel()
        searchAnimator = null
        _binding = null
    }
}
