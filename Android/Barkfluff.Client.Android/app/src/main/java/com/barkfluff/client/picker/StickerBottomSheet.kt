package com.barkfluff.client.picker

import android.os.Bundle
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.LinearLayoutManager
import barkfluff.files.FilesApiOuterClass.StickerInfo
import barkfluff.files.FilesApiOuterClass.StickerPackInfo
import com.barkfluff.client.adapter.StickerAdapter
import com.barkfluff.client.adapter.StickerPackTabAdapter
import com.barkfluff.client.databinding.BottomSheetStickersBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.repository.ChatRepository
import com.google.android.material.bottomsheet.BottomSheetBehavior
import com.google.android.material.bottomsheet.BottomSheetDialogFragment
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class StickerBottomSheet : BottomSheetDialogFragment() {

    private var _binding: BottomSheetStickersBinding? = null
    private val binding get() = _binding!!

    private lateinit var stickerAdapter: StickerAdapter
    private lateinit var packTabAdapter: StickerPackTabAdapter
    private lateinit var chatRepository: ChatRepository
    private lateinit var grpcManager: GrpcManager

    private var onStickerSelected: ((StickerInfo) -> Unit)? = null
    private var stickerPacks: List<StickerPackInfo> = emptyList()

    companion object {
        private const val TAG = "StickerBottomSheet"

        fun newInstance(onStickerSelected: (StickerInfo) -> Unit): StickerBottomSheet {
            return StickerBottomSheet().also {
                it.onStickerSelected = onStickerSelected
            }
        }
    }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = BottomSheetStickersBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        val app = requireActivity().application as com.barkfluff.client.BarkFluffApplication
        grpcManager = app.grpcManager
        chatRepository = ChatRepository(requireContext(), grpcManager)

        setupAdapters()
        loadStickerPacks()
    }

    override fun onStart() {
        super.onStart()
        val behavior = BottomSheetBehavior.from(requireView().parent as View)
        behavior.peekHeight = (resources.displayMetrics.heightPixels * 0.5).toInt()
        behavior.state = BottomSheetBehavior.STATE_COLLAPSED
    }

    private fun setupAdapters() {
        stickerAdapter = StickerAdapter(
            getFileUrl = { fileId ->
                chatRepository.getFileDownloadUrl(fileId).getOrNull()
            },
            onStickerClick = { sticker ->
                onStickerSelected?.invoke(sticker)
                dismiss()
            }
        )

        binding.stickersRecyclerView.apply {
            layoutManager = GridLayoutManager(requireContext(), 4)
            adapter = stickerAdapter
        }

        packTabAdapter = StickerPackTabAdapter { pack ->
            loadStickersForPack(pack.id)
        }

        binding.stickerPacksRecyclerView.apply {
            layoutManager = LinearLayoutManager(requireContext(), LinearLayoutManager.HORIZONTAL, false)
            adapter = packTabAdapter
        }
    }

    private fun loadStickerPacks() {
        binding.loadingProgress.visibility = View.VISIBLE
        binding.emptyStateLayout.visibility = View.GONE
        binding.stickersRecyclerView.visibility = View.GONE

        lifecycleScope.launch {
            try {
                val packs = withContext(Dispatchers.IO) {
                    grpcManager.listStickerPacks()
                }

                if (packs.isNullOrEmpty()) {
                    binding.loadingProgress.visibility = View.GONE
                    binding.emptyStateLayout.visibility = View.VISIBLE
                    return@launch
                }

                stickerPacks = packs
                packTabAdapter.setPacks(packs)
                binding.loadingProgress.visibility = View.GONE
                binding.stickersRecyclerView.visibility = View.VISIBLE

                // Load stickers for the first pack
                loadStickersForPack(packs.first().id)
            } catch (e: Exception) {
                Log.e(TAG, "Error loading sticker packs", e)
                binding.loadingProgress.visibility = View.GONE
                binding.emptyStateLayout.visibility = View.VISIBLE
            }
        }
    }

    private fun loadStickersForPack(packId: String) {
        lifecycleScope.launch {
            try {
                val stickers = withContext(Dispatchers.IO) {
                    grpcManager.getStickerPack(packId)
                }

                if (stickers != null) {
                    stickerAdapter.setStickers(stickers)
                    binding.stickersRecyclerView.scrollToPosition(0)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading stickers for pack $packId", e)
            }
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
