package com.barkfluff.client

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import androidx.core.os.bundleOf
import com.barkfluff.client.databinding.SheetLegalConsentBinding
import com.barkfluff.client.utils.LegalDocsRepository
import com.barkfluff.client.utils.MarkdownRenderer
import com.google.android.material.bottomsheet.BottomSheetBehavior
import com.google.android.material.bottomsheet.BottomSheetDialog
import com.google.android.material.bottomsheet.BottomSheetDialogFragment
import com.google.android.material.tabs.TabLayout

/**
 * Соглашение и политика конфиденциальности перед началом использования.
 *
 * Два режима:
 * - согласие (по умолчанию) — чекбокс + «Принять»/«Отмена», результат уходит через
 *   [RESULT_KEY]; принятая редакция сохраняется вызывающей стороной;
 * - read-only ([ARG_READ_ONLY]) — только чтение и «Закрыть».
 */
class LegalConsentBottomSheet : BottomSheetDialogFragment() {

    private var _binding: SheetLegalConsentBinding? = null
    private val binding get() = _binding!!

    private val readOnly: Boolean get() = arguments?.getBoolean(ARG_READ_ONLY) == true
    private val initialTab: Int get() = arguments?.getInt(ARG_INITIAL_TAB) ?: TAB_TERMS

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = SheetLegalConsentBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        // В режиме согласия закрыть лист свайпом/кнопкой «назад» нельзя: решение обязательно.
        isCancelable = readOnly

        binding.consentPanel.visibility = if (readOnly) View.GONE else View.VISIBLE
        binding.closeButton.visibility = if (readOnly) View.VISIBLE else View.GONE

        binding.legalTabs.addOnTabSelectedListener(object : TabLayout.OnTabSelectedListener {
            override fun onTabSelected(tab: TabLayout.Tab) = showDoc(tab.position)
            override fun onTabUnselected(tab: TabLayout.Tab) = Unit
            override fun onTabReselected(tab: TabLayout.Tab) = Unit
        })

        binding.acceptCheckBox.setOnCheckedChangeListener { _, checked ->
            binding.acceptButton.isEnabled = checked
        }

        binding.acceptButton.setOnClickListener {
            parentFragmentManager.setFragmentResult(RESULT_KEY, bundleOf(RESULT_ACCEPTED to true))
            dismiss()
        }
        binding.declineButton.setOnClickListener { dismiss() }
        binding.closeButton.setOnClickListener { dismiss() }

        binding.legalTabs.getTabAt(initialTab)?.select()
        showDoc(initialTab)
    }

    override fun onStart() {
        super.onStart()
        dialog?.window?.setDimAmount(0.42f)
        (dialog as? BottomSheetDialog)?.let { sheetDialog ->
            sheetDialog.findViewById<FrameLayout>(com.google.android.material.R.id.design_bottom_sheet)
                ?.let { sheet ->
                    sheet.setBackgroundResource(R.drawable.bg_create_chat_sheet)
                    // Документ длинный — открываем сразу развёрнутым и запрещаем сворачивание,
                    // иначе половина текста остаётся за краем экрана.
                    sheet.layoutParams = sheet.layoutParams.apply { height = ViewGroup.LayoutParams.MATCH_PARENT }
                }
            sheetDialog.behavior.apply {
                state = BottomSheetBehavior.STATE_EXPANDED
                skipCollapsed = true
                isDraggable = readOnly
            }
        }
    }

    private fun showDoc(position: Int) {
        val doc = if (position == TAB_PRIVACY) LegalDocsRepository.DOC_PRIVACY else LegalDocsRepository.DOC_TERMS
        val text = runCatching { LegalDocsRepository.load(requireContext(), doc) }
            .getOrElse { getString(R.string.legal_load_error) }

        MarkdownRenderer.applyTo(binding.legalText, text)
        binding.legalScroll.scrollTo(0, 0)
    }

    override fun onDestroyView() {
        _binding = null
        super.onDestroyView()
    }

    companion object {
        const val TAG = "legal_consent_sheet"
        const val RESULT_KEY = "legal_consent_result"
        const val RESULT_ACCEPTED = "accepted"

        const val TAB_TERMS = 0
        const val TAB_PRIVACY = 1

        private const val ARG_READ_ONLY = "read_only"
        private const val ARG_INITIAL_TAB = "initial_tab"

        /** Режим согласия: пользователь обязан принять или отказаться. */
        fun forConsent() = LegalConsentBottomSheet().apply {
            arguments = bundleOf(ARG_READ_ONLY to false, ARG_INITIAL_TAB to TAB_TERMS)
        }

        /** Режим чтения: открывается на указанном табе, без запроса согласия. */
        fun forReading(tab: Int) = LegalConsentBottomSheet().apply {
            arguments = bundleOf(ARG_READ_ONLY to true, ARG_INITIAL_TAB to tab)
        }
    }
}
