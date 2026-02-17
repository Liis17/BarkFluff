package com.barkfluff.android.ui.login

import android.graphics.Color
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.view.animation.AnimationUtils
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.navigation.NavOptions
import androidx.navigation.fragment.findNavController
import com.barkfluff.android.R
import com.barkfluff.android.databinding.FragmentLoginBinding
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch

@AndroidEntryPoint
class LoginFragment : Fragment() {

    private var _binding: FragmentLoginBinding? = null
    private val binding get() = _binding!!
    private val viewModel: LoginViewModel by viewModels()

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentLoginBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        binding.btnLogin.setOnClickListener {
            val login = binding.etLogin.text?.toString()?.trim() ?: ""
            val password = binding.etPassword.text?.toString() ?: ""
            val otp = if (binding.layoutOtp.visibility == View.VISIBLE)
                binding.etOtp.text?.toString()?.trim()
            else null

            if (login.isEmpty() || password.isEmpty()) {
                binding.tvError.visibility = View.VISIBLE
                binding.tvError.text = "Введите логин и пароль"
                return@setOnClickListener
            }
            viewModel.login(login, password, otp)
        }

        binding.tvChangeServer.setOnClickListener {
            findNavController().navigate(
                R.id.action_login_to_serverSelection,
                null,
                NavOptions.Builder()
                    .setPopUpTo(R.id.loginFragment, true)
                    .build()
            )
        }

        viewLifecycleOwner.lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.STARTED) {
                viewModel.uiState.collect { state ->
                    binding.tvServerName.text = state.serverName

                    if (state.serverColorMain.isNotEmpty()) {
                        try {
                            val color = Color.parseColor(
                                if (state.serverColorMain.startsWith("#")) state.serverColorMain
                                else "#${state.serverColorMain}"
                            )
                            binding.viewColorBar.setBackgroundColor(color)
                        } catch (_: IllegalArgumentException) {}
                    }

                    binding.btnLogin.isEnabled = !state.isLoading
                    binding.progressBar.visibility =
                        if (state.isLoading) View.VISIBLE else View.GONE

                    val otpCurrentlyVisible = binding.layoutOtp.visibility == View.VISIBLE
                    if (state.showOtpField && !otpCurrentlyVisible) {
                        binding.layoutOtp.visibility = View.VISIBLE
                        val anim = AnimationUtils.loadAnimation(requireContext(), android.R.anim.fade_in)
                        binding.layoutOtp.startAnimation(anim)
                        binding.etOtp.requestFocus()
                    } else if (!state.showOtpField) {
                        binding.layoutOtp.visibility = View.GONE
                    }

                    if (state.otpError) {
                        binding.layoutOtp.error = "Неверный код"
                    } else {
                        binding.layoutOtp.error = null
                    }

                    if (state.error != null) {
                        binding.tvError.visibility = View.VISIBLE
                        binding.tvError.text = state.error
                    } else {
                        binding.tvError.visibility = View.GONE
                    }

                    if (state.navigateToChatList) {
                        viewModel.onNavigatedToChatList()
                        findNavController().navigate(
                            R.id.action_login_to_chatList,
                            null,
                            NavOptions.Builder()
                                .setPopUpTo(R.id.loginFragment, true)
                                .build()
                        )
                    }
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        viewModel.reloadServerInfo()
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
