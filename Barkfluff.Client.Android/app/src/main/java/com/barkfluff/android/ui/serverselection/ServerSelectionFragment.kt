package com.barkfluff.android.ui.serverselection

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.navigation.fragment.findNavController
import com.barkfluff.android.R
import com.barkfluff.android.databinding.FragmentServerSelectionBinding
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch

@AndroidEntryPoint
class ServerSelectionFragment : Fragment() {

    private var _binding: FragmentServerSelectionBinding? = null
    private val binding get() = _binding!!
    private val viewModel: ServerSelectionViewModel by viewModels()
    private lateinit var adapter: ServerListAdapter

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentServerSelectionBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        adapter = ServerListAdapter { server ->
            viewModel.connectToServer(server.beaconHost, server.beaconPort)
        }
        binding.rvServers.adapter = adapter

        binding.btnConnect.setOnClickListener {
            val input = binding.etServerAddress.text?.toString()?.trim() ?: return@setOnClickListener
            if (input.isEmpty()) {
                binding.tvError.visibility = View.VISIBLE
                binding.tvError.text = "Введите адрес сервера"
                return@setOnClickListener
            }
            val (host, port) = parseHostPort(input) ?: run {
                binding.tvError.visibility = View.VISIBLE
                binding.tvError.text = "Неверный формат: используйте host:port"
                return@setOnClickListener
            }
            viewModel.connectToServer(host, port)
        }

        viewLifecycleOwner.lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.STARTED) {
                viewModel.uiState.collect { state ->
                    binding.progressBar.visibility =
                        if (state.isLoadingList) View.VISIBLE else View.GONE
                    binding.progressConnecting.visibility =
                        if (state.isConnecting) View.VISIBLE else View.GONE
                    binding.btnConnect.isEnabled = !state.isConnecting

                    adapter.submitList(state.servers)

                    if (state.error != null) {
                        binding.tvError.visibility = View.VISIBLE
                        binding.tvError.text = state.error
                    } else {
                        binding.tvError.visibility = View.GONE
                    }

                    if (state.navigateToLogin) {
                        viewModel.onNavigatedToLogin()
                        val prevDestId = findNavController().previousBackStackEntry?.destination?.id
                        if (prevDestId == R.id.loginFragment) {
                            // Came from Login (changing server) — just go back; LoginVM will reload
                            findNavController().popBackStack()
                        } else {
                            // Came from Welcome — clear Welcome and go to Login
                            findNavController().navigate(R.id.action_serverSelection_to_login)
                        }
                    }
                }
            }
        }
    }

    private fun parseHostPort(input: String): Pair<String, Int>? {
        val lastColon = input.lastIndexOf(':')
        if (lastColon <= 0) return null
        val host = input.substring(0, lastColon)
        val port = input.substring(lastColon + 1).toIntOrNull() ?: return null
        return host to port
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
