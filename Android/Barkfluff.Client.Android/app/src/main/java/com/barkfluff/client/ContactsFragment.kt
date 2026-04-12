package com.barkfluff.client

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import com.barkfluff.client.databinding.FragmentContactsBinding

/**
 * [UNUSED] ContactsFragment — остаётся в проекте как неиспользуемый объект.
 * Вкладка «Контакты» удалена из нижней навигации, развитие контактов не предвидится.
 */
class ContactsFragment : Fragment() {

    private var _binding: FragmentContactsBinding? = null
    private val binding get() = _binding!!

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentContactsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
