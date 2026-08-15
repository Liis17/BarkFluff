package com.barkfluff.client.dialog

import android.app.Dialog
import android.os.Bundle
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.R
import com.barkfluff.client.adapter.ForwardChatPickerAdapter
import com.barkfluff.client.databinding.BottomSheetForwardChatsBinding
import com.barkfluff.client.repository.ChatRepository
import com.google.android.material.bottomsheet.BottomSheetBehavior
import com.google.android.material.bottomsheet.BottomSheetDialog
import com.google.android.material.bottomsheet.BottomSheetDialogFragment
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.launch

/**
 * Модалка для пересылки сообщений в один или несколько чатов.
 * Принимает ID исходных сообщений и отправляет их одним сообщением в каждый выбранный чат.
 */
class ForwardChatPickerBottomSheet : BottomSheetDialogFragment() {

    companion object {
        private const val TAG = "ForwardPicker"
        private const val ARG_MESSAGE_IDS = "messageIds"

        fun newInstance(messageId: Long): ForwardChatPickerBottomSheet = newInstance(longArrayOf(messageId))

        fun newInstance(messageIds: LongArray): ForwardChatPickerBottomSheet {
            return ForwardChatPickerBottomSheet().apply {
                arguments = Bundle().apply {
                    putLongArray(ARG_MESSAGE_IDS, messageIds)
                }
            }
        }
    }

    private var _binding: BottomSheetForwardChatsBinding? = null
    private val binding get() = _binding!!

    private lateinit var chatRepository: ChatRepository
    private lateinit var adapter: ForwardChatPickerAdapter
    private var messageIds: LongArray = longArrayOf()

    override fun onCreateDialog(savedInstanceState: Bundle?): Dialog {
        val dialog = super.onCreateDialog(savedInstanceState) as BottomSheetDialog
        dialog.setOnShowListener {
            val sheet = dialog.findViewById<View>(com.google.android.material.R.id.design_bottom_sheet)
            sheet?.let {
                BottomSheetBehavior.from(it).apply {
                    state = BottomSheetBehavior.STATE_EXPANDED
                    skipCollapsed = true
                }
            }
        }
        return dialog
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?
    ): View {
        _binding = BottomSheetForwardChatsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        messageIds = arguments?.getLongArray(ARG_MESSAGE_IDS) ?: longArrayOf()
        if (messageIds.isEmpty()) {
            dismissAllowingStateLoss()
            return
        }

        val app = requireActivity().application as BarkFluffApplication
        val grpcManager = app.grpcManager
        chatRepository = ChatRepository(requireContext(), grpcManager)

        adapter = ForwardChatPickerAdapter(
            getFileUrl = { fileId -> chatRepository.getFileDownloadUrl(fileId).getOrNull() },
            onSelectionChanged = { count ->
                binding.sendButton.isEnabled = count > 0
                binding.sendButton.text = if (count > 0) {
                    getString(R.string.forward_button_count, count)
                } else {
                    getString(R.string.forward_button)
                }
            }
        )

        binding.chatsRecyclerView.apply {
            layoutManager = LinearLayoutManager(requireContext())
            adapter = this@ForwardChatPickerBottomSheet.adapter
        }

        binding.sendButton.setOnClickListener {
            performForward()
        }

        loadChats(grpcManager)
    }

    private fun loadChats(grpcManager: com.barkfluff.client.grpc.GrpcManager) {
        binding.loadingProgress.visibility = View.VISIBLE
        binding.chatsRecyclerView.visibility = View.GONE
        lifecycleScope.launch {
            val result = grpcManager.getChats()
            binding.loadingProgress.visibility = View.GONE
            binding.chatsRecyclerView.visibility = View.VISIBLE
            if (result.isSuccess) {
                adapter.submitList(result.getOrNull().orEmpty())
            } else {
                Toast.makeText(
                    requireContext(),
                    getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()),
                    Toast.LENGTH_SHORT
                ).show()
            }
        }
    }

    private fun performForward() {
        val selected = adapter.getSelectedIds()
        if (selected.isEmpty()) return

        val comment = binding.commentEditText.text?.toString()?.trim().orEmpty()

        binding.sendButton.isEnabled = false
        binding.sendButton.text = getString(R.string.forward_loading)

        lifecycleScope.launch {
            val results = selected.map { chatId ->
                async {
                    // Пачка уезжает одним сообщением на чат, а не N сообщениями:
                    // получатель видит пересылку так же, как её собрал отправитель.
                    chatRepository.sendMessage(
                        chatId = chatId,
                        text = comment,
                        forwardedMessageIds = messageIds.toList()
                    )
                }
            }.awaitAll()

            val successCount = results.count { it.isSuccess }
            val failCount = results.size - successCount

            val msg = when {
                failCount == 0 -> getString(
                    R.string.forward_success,
                    resources.getQuantityString(R.plurals.forward_chat_count, successCount, successCount)
                )
                successCount == 0 -> getString(R.string.forward_failed)
                else -> getString(R.string.forward_partial, successCount, failCount)
            }
            Toast.makeText(requireContext(), msg, Toast.LENGTH_SHORT).show()

            if (failCount > 0) {
                Log.w(TAG, "Some forwards failed: ${results.filter { it.isFailure }.map { it.exceptionOrNull()?.message }}")
            }

            dismissAllowingStateLoss()
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
