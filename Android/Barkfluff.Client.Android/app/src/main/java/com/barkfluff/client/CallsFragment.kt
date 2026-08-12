package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.text.format.DateUtils
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.fragment.app.Fragment
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import barkfluff.calls.CallsApiOuterClass
import com.barkfluff.client.adapter.CallHistoryAdapter
import com.barkfluff.client.calls.CallActivity
import com.barkfluff.client.calls.CallExtras
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.FragmentCallsBinding
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.launch

class CallsFragment : Fragment() {

    private var _binding: FragmentCallsBinding? = null
    private val binding get() = _binding!!

    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var adapter: CallHistoryAdapter

    private var missedOnly = false

    companion object {
        private const val TAG = "CallsFragment"
    }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentCallsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        val app = requireActivity().application as BarkFluffApplication
        globalParam = GlobalParam(requireContext())
        grpcManager = app.grpcManager

        adapter = CallHistoryAdapter(
            onRowClick = { openChat(it) },
            onCallClick = { startCall(it) }
        )
        binding.callsRecyclerView.layoutManager = LinearLayoutManager(requireContext())
        binding.callsRecyclerView.adapter = adapter

        binding.filterChipGroup.setOnCheckedStateChangeListener { _, checkedIds ->
            val newMissedOnly = checkedIds.contains(R.id.filterMissedChip)
            if (newMissedOnly != missedOnly) {
                missedOnly = newMissedOnly
                loadHistory()
            }
        }

        loadHistory()
    }

    private fun loadHistory() {
        viewLifecycleOwner.lifecycleScope.launch {
            if (!ensureCallsClient()) {
                showEmpty()
                return@launch
            }

            val filter = if (missedOnly) {
                CallsApiOuterClass.CallHistoryFilter.CALL_HISTORY_MISSED
            } else {
                CallsApiOuterClass.CallHistoryFilter.CALL_HISTORY_ALL
            }

            val result = (requireActivity().application as BarkFluffApplication)
                .callRepository.listCallHistory(filter, limit = 50)

            result.onSuccess { response ->
                val rows = resolveRows(response.itemsList)
                if (_binding == null) return@onSuccess
                adapter.submitList(rows)
                if (rows.isEmpty()) showEmpty() else showList()
            }.onFailure { error ->
                Log.e(TAG, "Не удалось загрузить историю звонков", error)
                if (_binding == null) return@onFailure
                showEmpty()
            }
        }
    }

    /** Резолвит имена: личные — через getUserData, групповые — через список чатов. */
    private suspend fun resolveRows(
        items: List<CallsApiOuterClass.CallHistoryItem>
    ): List<CallHistoryAdapter.Row> = coroutineScope {
        // Заголовки групповых чатов из списка чатов (один запрос).
        val groupTitles: Map<String, String> = if (items.any { it.isGroup }) {
            grpcManager.getChats().getOrNull()
                ?.associate { it.id to it.title.ifBlank { getString(R.string.call_group_title) } }
                ?: emptyMap()
        } else {
            emptyMap()
        }

        items.map { item ->
            async {
                val title = if (item.isGroup) {
                    groupTitles[item.chatId] ?: getString(R.string.call_group_title)
                } else {
                    resolvePeerName(item.peerUserId)
                }
                buildRow(item, title)
            }
        }.awaitAll()
    }

    private suspend fun resolvePeerName(peerUserId: Long): String {
        if (peerUserId <= 0L) return getString(R.string.call_default_user)
        val user = grpcManager.getUserData(peerUserId).getOrNull() ?: return getString(R.string.call_default_user)
        return "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
    }

    private fun buildRow(item: CallsApiOuterClass.CallHistoryItem, title: String): CallHistoryAdapter.Row {
        val isMissed = item.endReason == CallsApiOuterClass.CallEndReason.CALL_END_MISSED
        val isVideo = item.mediaType == CallsApiOuterClass.CallMediaType.CALL_MEDIA_VIDEO

        val directionLabel = when {
            isMissed -> getString(R.string.call_direction_missed)
            item.direction == CallsApiOuterClass.CallDirection.CALL_DIRECTION_OUTGOING -> getString(R.string.call_direction_outgoing)
            else -> getString(R.string.call_direction_incoming)
        }

        val time = DateUtils.getRelativeTimeSpanString(
            item.startedAt.seconds * 1000,
            System.currentTimeMillis(),
            DateUtils.MINUTE_IN_MILLIS
        )

        val subtitle = buildString {
            append(directionLabel)
            append(" · ")
            append(time)
            if (!isMissed && item.durationSeconds > 0) {
                append(" · ")
                append(formatDuration(item.durationSeconds))
            }
        }

        return CallHistoryAdapter.Row(
            callId = item.callId,
            chatId = item.chatId,
            isGroup = item.isGroup,
            peerUserId = item.peerUserId,
            title = title,
            subtitle = subtitle,
            isMissed = isMissed,
            isVideo = isVideo
        )
    }

    private fun formatDuration(seconds: Long): String {
        val m = seconds / 60
        val s = seconds % 60
        return "%d:%02d".format(m, s)
    }

    private fun openChat(row: CallHistoryAdapter.Row) {
        if (row.isGroup) {
            startActivity(Intent(requireContext(), ChatActivity::class.java).apply {
                putExtra("chat_id", row.chatId)
                putExtra("chat_title", row.title)
                putExtra("is_group_chat", true)
            })
            return
        }

        viewLifecycleOwner.lifecycleScope.launch {
            val chatId = grpcManager.getPersonChatId(row.peerUserId).getOrNull()
            if (chatId.isNullOrBlank() || _binding == null) {
                if (_binding != null) {
                    Toast.makeText(requireContext(), R.string.call_open_chat_failed, Toast.LENGTH_SHORT).show()
                }
                return@launch
            }
            startActivity(Intent(requireContext(), ChatActivity::class.java).apply {
                putExtra("chat_id", chatId)
                putExtra("chat_title", row.title)
                putExtra("is_group_chat", false)
                putExtra("other_user_id", row.peerUserId)
            })
        }
    }

    private fun startCall(row: CallHistoryAdapter.Row) {
        viewLifecycleOwner.lifecycleScope.launch {
            if (!ensureCallsClient()) return@launch

            val app = requireActivity().application as BarkFluffApplication
            val mediaType = if (row.isVideo) {
                CallsApiOuterClass.CallMediaType.CALL_MEDIA_VIDEO
            } else {
                CallsApiOuterClass.CallMediaType.CALL_MEDIA_AUDIO
            }

            val result = if (row.isGroup) {
                app.callRepository.initiateGroup(row.chatId, mediaType)
            } else {
                app.callRepository.initiateDirect(row.peerUserId, mediaType)
            }

            result.onSuccess { response ->
                if (_binding == null) return@onSuccess
                startActivity(Intent(requireContext(), CallActivity::class.java).apply {
                    putExtra(CallExtras.EXTRA_CALL_ID, response.callId)
                    putExtra(CallExtras.EXTRA_CALLER_NAME, row.title)
                    putExtra(CallExtras.EXTRA_CHAT_ID, row.chatId)
                    putExtra(CallExtras.EXTRA_MEDIA_TYPE, if (row.isVideo) "video" else "audio")
                    putExtra(CallExtras.EXTRA_LIVEKIT_URL, response.livekitUrl.ifBlank { globalParam.livekitUrl })
                    putExtra(CallExtras.EXTRA_ACCESS_TOKEN, response.accessToken)
                })
            }.onFailure { error ->
                Log.e(TAG, "Не удалось начать звонок", error)
                if (_binding != null) {
                    Toast.makeText(requireContext(), R.string.call_start_failed, Toast.LENGTH_SHORT).show()
                }
            }
        }
    }

    private fun ensureCallsClient(): Boolean {
        if (grpcManager.callsClient != null) return true

        val callsAddress = globalParam.socketCalls
        if (callsAddress.isBlank()) return false

        return grpcManager.createCallsClient(callsAddress, requireContext(), includeDeviceInfo = true).isSuccess
    }

    private fun showEmpty() {
        if (_binding == null) return
        binding.emptyTitle.text = getString(if (missedOnly) R.string.call_empty_missed else R.string.call_empty_all)
        binding.emptyState.visibility = View.VISIBLE
        binding.callsRecyclerView.visibility = View.GONE
    }

    private fun showList() {
        if (_binding == null) return
        binding.emptyState.visibility = View.GONE
        binding.callsRecyclerView.visibility = View.VISIBLE
    }

    override fun onDestroyView() {
        _binding = null
        super.onDestroyView()
    }
}
