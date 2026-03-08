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
import com.barkfluff.client.databinding.BottomSheetDeviceDetailsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.google.android.material.bottomsheet.BottomSheetDialog
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.launch

class DevicesActivity : AppCompatActivity() {

    private lateinit var binding: ActivityDevicesBinding
    private lateinit var grpcManager: GrpcManager
    private lateinit var globalParam: GlobalParam
    private lateinit var deviceAdapter: DeviceAdapter

    private var allSessions: List<GrpcManager.SessionData> = emptyList()

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
        setupButtons()
        loadSessions()
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }
    }

    private fun setupRecyclerView() {
        deviceAdapter = DeviceAdapter { session ->
            showDeviceDetailsBottomSheet(session)
        }

        binding.recyclerDevices.apply {
            layoutManager = LinearLayoutManager(this@DevicesActivity)
            adapter = deviceAdapter
            isNestedScrollingEnabled = false
        }
    }

    private fun setupButtons() {
        binding.buttonConnectDevice.setOnClickListener {
            // TODO: Navigate to QR scanner activity
            Toast.makeText(this, "Сканирование QR-кода (скоро)", Toast.LENGTH_SHORT).show()
        }

        binding.buttonTerminateAll.setOnClickListener {
            showTerminateAllDialog()
        }
    }

    private fun loadSessions() {
        binding.progressLoading.visibility = View.VISIBLE

        lifecycleScope.launch {
            val result = grpcManager.getActiveSessions()
            binding.progressLoading.visibility = View.GONE

            if (result.isSuccess) {
                allSessions = result.getOrNull() ?: emptyList()
                updateUI()
            } else {
                Log.e(TAG, "Ошибка загрузки сессий", result.exceptionOrNull())
                Toast.makeText(this@DevicesActivity, "Ошибка загрузки сессий", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun updateUI() {
        val currentSession = allSessions.find { it.deviceId == globalParam.deviceId }
        val otherSessions = allSessions.filter { it.deviceId != globalParam.deviceId }

        // Заполняем текущее устройство
        if (currentSession != null) {
            val deviceName = currentSession.customName.ifEmpty { currentSession.originalName }
            binding.textCurrentDeviceName.text = deviceName.ifEmpty { "Неизвестное устройство" }
            binding.textCurrentDeviceInfo.text = currentSession.appName
            binding.cardCurrentDevice.visibility = View.VISIBLE
        } else {
            binding.cardCurrentDevice.visibility = View.GONE
        }

        // Обновляем кнопку завершения всех сессий
        binding.buttonTerminateAll.isEnabled = otherSessions.isNotEmpty()

        // Показываем список других устройств
        if (otherSessions.isNotEmpty()) {
            binding.textOtherDevicesTitle.visibility = View.VISIBLE
            deviceAdapter.submitList(otherSessions)
        } else {
            binding.textOtherDevicesTitle.visibility = View.GONE
            deviceAdapter.submitList(emptyList())
        }
    }

    private fun showDeviceDetailsBottomSheet(session: GrpcManager.SessionData) {
        val bottomSheet = BottomSheetDialog(this)
        val sheetBinding = BottomSheetDeviceDetailsBinding.inflate(layoutInflater)
        bottomSheet.setContentView(sheetBinding.root)

        val deviceName = session.customName.ifEmpty { session.originalName }.ifEmpty { "Неизвестное устройство" }

        sheetBinding.textDeviceTitle.text = deviceName
        sheetBinding.textAppName.text = session.appName
        sheetBinding.textOS.text = session.os
        sheetBinding.textLocation.text = session.location.ifEmpty { "Неизвестно" }
        sheetBinding.textDeviceId.text = session.deviceId

        sheetBinding.buttonTerminate.setOnClickListener {
            bottomSheet.dismiss()
            showRemoveSessionDialog(session)
        }

        bottomSheet.show()
    }

    private fun showRemoveSessionDialog(session: GrpcManager.SessionData) {
        val name = session.customName.ifEmpty { session.originalName }.ifEmpty { "Неизвестное устройство" }

        MaterialAlertDialogBuilder(this)
            .setTitle("Завершить сессию")
            .setMessage("Завершить сессию на устройстве \"$name\"?")
            .setPositiveButton("Завершить") { _, _ ->
                removeSession(session.deviceId)
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    private fun showTerminateAllDialog() {
        val otherCount = allSessions.count { it.deviceId != globalParam.deviceId }
        if (otherCount == 0) return

        MaterialAlertDialogBuilder(this)
            .setTitle("Завершить все сессии")
            .setMessage("Завершить все сессии на $otherCount других устройствах? Вы останетесь в системе только на этом устройстве.")
            .setPositiveButton("Завершить все") { _, _ ->
                terminateAllOtherSessions()
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    private fun removeSession(deviceId: String) {
        lifecycleScope.launch {
            val result = grpcManager.removeActiveSession(deviceId)
            if (result.isSuccess) {
                allSessions = allSessions.filter { it.deviceId != deviceId }
                updateUI()
                Toast.makeText(this@DevicesActivity, "Сессия завершена", Toast.LENGTH_SHORT).show()
            } else {
                Toast.makeText(this@DevicesActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun terminateAllOtherSessions() {
        val otherDeviceIds = allSessions
            .filter { it.deviceId != globalParam.deviceId }
            .map { it.deviceId }

        if (otherDeviceIds.isEmpty()) return

        binding.progressLoading.visibility = View.VISIBLE

        lifecycleScope.launch {
            var successCount = 0
            var failCount = 0

            for (deviceId in otherDeviceIds) {
                val result = grpcManager.removeActiveSession(deviceId)
                if (result.isSuccess) {
                    successCount++
                } else {
                    failCount++
                    Log.e(TAG, "Ошибка завершения сессии $deviceId", result.exceptionOrNull())
                }
            }

            binding.progressLoading.visibility = View.GONE

            if (failCount == 0) {
                allSessions = allSessions.filter { it.deviceId == globalParam.deviceId }
                updateUI()
                Toast.makeText(this@DevicesActivity, "Все сессии завершены ($successCount)", Toast.LENGTH_SHORT).show()
            } else {
                loadSessions()
                Toast.makeText(
                    this@DevicesActivity,
                    "Завершено: $successCount, ошибок: $failCount",
                    Toast.LENGTH_SHORT
                ).show()
            }
        }
    }
}
