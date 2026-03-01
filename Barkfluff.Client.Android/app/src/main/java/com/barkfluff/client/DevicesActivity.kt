package com.barkfluff.client

import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.DeviceAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityDevicesBinding
import com.barkfluff.client.grpc.GrpcManager
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.launch

class DevicesActivity : AppCompatActivity() {

    private lateinit var binding: ActivityDevicesBinding
    private lateinit var grpcManager: GrpcManager
    private lateinit var globalParam: GlobalParam
    private lateinit var deviceAdapter: DeviceAdapter

    companion object {
        private const val TAG = "DevicesActivity"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityDevicesBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager
        globalParam = GlobalParam(this)

        setupToolbar()
        setupRecyclerView()
        loadSessions()
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }
    }

    private fun setupRecyclerView() {
        deviceAdapter = DeviceAdapter(globalParam.deviceId) { session ->
            showRemoveSessionDialog(session)
        }

        binding.recyclerDevices.apply {
            layoutManager = LinearLayoutManager(this@DevicesActivity)
            adapter = deviceAdapter
        }
    }

    private fun loadSessions() {
        binding.progressLoading.visibility = View.VISIBLE

        lifecycleScope.launch {
            val result = grpcManager.getActiveSessions()
            binding.progressLoading.visibility = View.GONE

            if (result.isSuccess) {
                val sessions = result.getOrNull() ?: emptyList()
                // Текущее устройство первым
                val sorted = sessions.sortedByDescending { it.deviceId == globalParam.deviceId }
                deviceAdapter.submitList(sorted)
            } else {
                Log.e(TAG, "Ошибка загрузки сессий", result.exceptionOrNull())
                Toast.makeText(this@DevicesActivity, "Ошибка загрузки сессий", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun showRemoveSessionDialog(session: GrpcManager.SessionData) {
        val name = session.customName.ifEmpty { session.originalName }.ifEmpty { "Неизвестное устройство" }

        MaterialAlertDialogBuilder(this)
            .setTitle("Завершить сессию")
            .setMessage("Завершить сессию на устройстве \"$name\"?")
            .setPositiveButton("Завершить") { _, _ ->
                lifecycleScope.launch {
                    val result = grpcManager.removeActiveSession(session.deviceId)
                    if (result.isSuccess) {
                        val currentList = deviceAdapter.currentList.toMutableList()
                        currentList.removeAll { it.deviceId == session.deviceId }
                        deviceAdapter.submitList(currentList)
                    } else {
                        Toast.makeText(this@DevicesActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                    }
                }
            }
            .setNegativeButton("Отмена", null)
            .show()
    }
}
