package com.barkfluff.client

import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.databinding.ActivityFastAuthConfirmBinding
import kotlinx.coroutines.launch

class FastAuthConfirmActivity : AppCompatActivity() {

    private lateinit var binding: ActivityFastAuthConfirmBinding
    private lateinit var grpcManager: com.barkfluff.client.grpc.GrpcManager

    private lateinit var fastAuthId: String
    private lateinit var confirmationCode: String

    companion object {
        private const val TAG = "FastAuthConfirmActivity"
        const val EXTRA_FAST_AUTH_ID = "fast_auth_id"
        const val EXTRA_CONFIRMATION_CODE = "confirmation_code"
        const val EXTRA_DEVICE_NAME = "device_name"
        const val EXTRA_OS = "os"
        const val EXTRA_APP_NAME = "app_name"
        const val EXTRA_APP_VERSION = "app_version"
        const val EXTRA_IP = "ip"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityFastAuthConfirmBinding.inflate(layoutInflater)
        setContentView(binding.root)

        grpcManager = (application as BarkFluffApplication).grpcManager

        fastAuthId = intent.getStringExtra(EXTRA_FAST_AUTH_ID) ?: run {
            finish()
            return
        }
        confirmationCode = intent.getStringExtra(EXTRA_CONFIRMATION_CODE) ?: run {
            finish()
            return
        }

        binding.toolbar.setNavigationOnClickListener { finish() }

        binding.textDeviceName.text = intent.getStringExtra(EXTRA_DEVICE_NAME) ?: getString(R.string.fast_auth_unknown)
        binding.textOs.text = intent.getStringExtra(EXTRA_OS) ?: getString(R.string.fast_auth_unknown)
        val appName = intent.getStringExtra(EXTRA_APP_NAME) ?: ""
        val appVersion = intent.getStringExtra(EXTRA_APP_VERSION) ?: ""
        binding.textApp.text = if (appVersion.isNotBlank()) {
            getString(R.string.fast_auth_app_info, appName, appVersion)
        } else appName
        binding.textIp.text = intent.getStringExtra(EXTRA_IP)?.takeIf { it.isNotBlank() }
            ?: getString(R.string.fast_auth_unknown)

        binding.buttonAccept.setOnClickListener { onAccept() }
        binding.buttonReject.setOnClickListener { onReject() }
    }

    private fun onAccept() {
        setButtonsEnabled(false)
        binding.progressLoading.visibility = View.VISIBLE

        lifecycleScope.launch {
            val result = grpcManager.acceptFastAuth(fastAuthId, confirmationCode)
            binding.progressLoading.visibility = View.GONE

            if (result.isSuccess) {
                setResult(RESULT_OK)
                finish()
            } else {
                val msg = result.exceptionOrNull()?.message ?: getString(R.string.error)
                Log.e(TAG, "Ошибка acceptFastAuth: $msg")
                Toast.makeText(
                    this@FastAuthConfirmActivity,
                    getString(R.string.fast_auth_error, msg),
                    Toast.LENGTH_LONG
                ).show()
                setButtonsEnabled(true)
            }
        }
    }

    private fun onReject() {
        setButtonsEnabled(false)
        binding.progressLoading.visibility = View.VISIBLE

        lifecycleScope.launch {
            grpcManager.rejectFastAuth(fastAuthId, confirmationCode)
            setResult(RESULT_OK)
            finish()
        }
    }

    private fun setButtonsEnabled(enabled: Boolean) {
        binding.buttonAccept.isEnabled = enabled
        binding.buttonReject.isEnabled = enabled
    }
}
