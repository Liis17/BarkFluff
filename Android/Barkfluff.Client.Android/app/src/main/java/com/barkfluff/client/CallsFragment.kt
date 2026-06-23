package com.barkfluff.client

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import com.barkfluff.client.databinding.FragmentCallsBinding

class CallsFragment : Fragment() {

    private var _binding: FragmentCallsBinding? = null
    private val binding get() = _binding!!

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentCallsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        binding.filterAllChip.setOnClickListener { renderEmptyState(missedOnly = false) }
        binding.filterMissedChip.setOnClickListener { renderEmptyState(missedOnly = true) }
        renderEmptyState(missedOnly = false)
    }

    private fun renderEmptyState(missedOnly: Boolean) {
        binding.emptyTitle.text = if (missedOnly) "Пропущенных звонков нет" else "Звонков пока нет"
    }

    override fun onDestroyView() {
        _binding = null
        super.onDestroyView()
    }
}