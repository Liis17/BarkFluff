package com.barkfluff.client

import android.content.Intent
import android.content.pm.PackageManager
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.DeviceAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityDevicesBinding
import com.barkfluff.client.databinding.BottomSheetDeviceDetailsBinding
import com.barkfluff.client.domain.gateway.AuthGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.domain.model.SessionData
import com.barkfluff.client.calls.CallEventsService
import com.barkfluff.client.crypto.PrekeyManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.repository.PrivateChatRepository
import com.barkfluff.client.repository.SecretChatRepository
import com.barkfluff.client.send.OutgoingMessageQueue
import com.barkfluff.client.utils.LogoutHelper
import com.barkfluff.client.utils.showImeAware
import com.google.android.material.bottomsheet.BottomSheetDialog
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch

@AndroidEntryPoint
class DevicesActivity : AppCompatActivity() {

    private lateinit var binding: ActivityDevicesBinding
    @javax.inject.Inject lateinit var userProfileGateway: UserProfileGateway
    @javax.inject.Inject lateinit var authGateway: AuthGateway
    @javax.inject.Inject lateinit var realtimeService: RealtimeService
    @javax.inject.Inject lateinit var callEventsService: CallEventsService
    @javax.inject.Inject lateinit var outgoingMessageQueue: OutgoingMessageQueue
    @javax.inject.Inject lateinit var chatCacheRepository: ChatCacheRepository
    @javax.inject.Inject lateinit var privateChatRepository: PrivateChatRepository
    @javax.inject.Inject lateinit var prekeyManager: PrekeyManager
    @javax.inject.Inject lateinit var secretChatRepository: SecretChatRepository
    private lateinit var globalParam: GlobalParam
    private lateinit var deviceAdapter: DeviceAdapter

    private var allSessions: List<SessionData> = emptyList()

    private val qrScannerLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == RESULT_OK) {
            loadSessions()
        }
    }

    private val cameraPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) {
            qrScannerLauncher.launch(Intent(this, QrScannerActivity::class.java))
        } else {
            Toast.makeText(this, R.string.camera_permission_settings, Toast.LENGTH_LONG).show()
        }
    }

    companion object {
        private const val TAG = "DevicesActivity"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityDevicesBinding.inflate(layoutInflater)
        setContentView(binding.root)

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

        // Обработчик клика на текущее устройство
        binding.cardCurrentDevice.setOnClickListener {
            val currentSession = allSessions.find { it.deviceId == globalParam.deviceId }
            currentSession?.let { showDeviceDetailsBottomSheet(it) }
        }
    }

    private fun setupButtons() {
        binding.buttonConnectDevice.setOnClickListener {
            if (checkSelfPermission(android.Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
                qrScannerLauncher.launch(Intent(this, QrScannerActivity::class.java))
            } else {
                cameraPermissionLauncher.launch(android.Manifest.permission.CAMERA)
            }
        }

        binding.buttonTerminateAll.setOnClickListener {
            showTerminateAllDialog()
        }
    }

    private fun loadSessions() {
        binding.progressLoading.visibility = View.VISIBLE

        lifecycleScope.launch {
            val result = userProfileGateway.getActiveSessions(this@DevicesActivity)
            binding.progressLoading.visibility = View.GONE

            if (result.isSuccess) {
                allSessions = result.getOrNull() ?: emptyList()
                updateUI()
            } else {
                Log.e(TAG, "Ошибка загрузки сессий", result.exceptionOrNull())
                Toast.makeText(this@DevicesActivity, R.string.devices_load_error, Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun updateUI() {
        val currentSession = allSessions.find { it.deviceId == globalParam.deviceId }
        val otherSessions = allSessions.filter { it.deviceId != globalParam.deviceId }

        // Заполняем текущее устройство
        if (currentSession != null) {
            binding.imageCurrentDeviceIcon.setImageResource(DeviceAdapter.getDeviceIcon(currentSession))
            binding.textCurrentDeviceName.text = DeviceAdapter.displayName(this, currentSession)
            val details = DeviceAdapter.details(currentSession)
            binding.textCurrentDeviceDetails.text = details
            binding.textCurrentDeviceDetails.visibility = if (details.isEmpty()) View.GONE else View.VISIBLE
            binding.cardCurrentDevice.visibility = View.VISIBLE
        } else {
            binding.cardCurrentDevice.visibility = View.GONE
        }

        // «Завершить другие сессии» — нижняя строка группы текущего устройства.
        val canTerminateOthers = otherSessions.isNotEmpty()
        binding.buttonTerminateAll.visibility = if (canTerminateOthers) View.VISIBLE else View.GONE
        binding.cardCurrentDevice.setBackgroundResource(
            if (canTerminateOthers) R.drawable.bg_settings_item_top else R.drawable.bg_settings_item_single,
        )
        binding.buttonTerminateAll.setBackgroundResource(
            if (currentSession != null) R.drawable.bg_settings_item_bottom else R.drawable.bg_settings_item_single,
        )

        binding.textOtherDevicesTitle.visibility = if (canTerminateOthers) View.VISIBLE else View.GONE
        deviceAdapter.submitList(otherSessions)
    }

    private fun showDeviceDetailsBottomSheet(session: SessionData) {
        val bottomSheet = BottomSheetDialog(this)
        val sheetBinding = BottomSheetDeviceDetailsBinding.inflate(layoutInflater)
        bottomSheet.setContentView(sheetBinding.root)

        // Отображаем имена устройства
        if (session.customName.isNotEmpty()) {
            // Есть кастомное имя - показываем его в заголовке, оригинальное ниже
            sheetBinding.textDeviceTitle.text = session.customName
            sheetBinding.textDeviceTitle.visibility = android.view.View.VISIBLE
            sheetBinding.textDeviceOriginalName.text = session.originalName
            sheetBinding.textDeviceOriginalName.visibility = android.view.View.VISIBLE
        } else {
            // Нет кастомного имени - показываем только оригинальное в заголовке
            sheetBinding.textDeviceTitle.text = DeviceAdapter.displayName(this, session)
            sheetBinding.textDeviceTitle.visibility = android.view.View.VISIBLE
            sheetBinding.textDeviceOriginalName.visibility = android.view.View.GONE
        }

        sheetBinding.textAppName.text = session.appName
        sheetBinding.textOS.text = session.os
        sheetBinding.textLocation.text = session.location.ifEmpty { getString(R.string.device_unknown) }
        sheetBinding.textDeviceId.text = session.deviceId

        // Кнопка переименования доступна для всех устройств
        sheetBinding.buttonRename.setOnClickListener {
            bottomSheet.dismiss()
            showRenameDeviceDialog(session)
        }

        sheetBinding.buttonTerminate.setOnClickListener {
            bottomSheet.dismiss()
            showRemoveSessionDialog(session)
        }

        bottomSheet.show()
    }

    private fun showRenameDeviceDialog(session: SessionData) {
        val builder = MaterialAlertDialogBuilder(this)
        builder.setTitle(R.string.device_rename)

        val input = android.widget.EditText(this)
        input.hint = getString(R.string.device_rename_hint)
        input.setText(session.customName.ifEmpty { session.originalName })
        builder.setView(input)

        builder.setPositiveButton(R.string.btn_save) { _, _ ->
            val newCustomName = input.text.toString().trim()
            if (newCustomName.isNotEmpty()) {
                renameDevice(session.deviceId, newCustomName)
            }
        }
        builder.setNegativeButton(R.string.btn_cancel, null)
        builder.showImeAware()
    }

    private fun renameDevice(deviceId: String, customName: String) {
        binding.progressLoading.visibility = View.VISIBLE

        lifecycleScope.launch {
            val result = userProfileGateway.renameDevice(deviceId, customName)
            binding.progressLoading.visibility = View.GONE

            if (result.isSuccess) {
                // Обновляем локальный список сессий
                allSessions = allSessions.map { session ->
                    if (session.deviceId == deviceId) {
                        session.copy(customName = customName)
                    } else {
                        session
                    }
                }
                updateUI()
                Toast.makeText(this@DevicesActivity, R.string.device_renamed, Toast.LENGTH_SHORT).show()
            } else {
                Toast.makeText(this@DevicesActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun showRemoveSessionDialog(session: SessionData) {
        val name = session.customName.ifEmpty { session.originalName }.ifEmpty { getString(R.string.device_unknown_name) }
        val isCurrentDevice = session.deviceId == globalParam.deviceId

        val message = if (isCurrentDevice) {
            getString(R.string.device_terminate_current_message)
        } else {
            getString(R.string.device_terminate_named_message, name)
        }

        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.device_terminate_session)
            .setMessage(message)
            .setPositiveButton(R.string.device_terminate_action) { _, _ ->
                if (isCurrentDevice) {
                    lifecycleScope.launch {
                        LogoutHelper.performFullLogout(
                            this@DevicesActivity,
                            authGateway,
                            realtimeService,
                            callEventsService,
                            outgoingMessageQueue,
                            chatCacheRepository,
                            privateChatRepository,
                            prekeyManager,
                            secretChatRepository,
                        )
                    }
                } else {
                    removeSession(session.deviceId)
                }
            }
            .setNegativeButton(R.string.btn_cancel, null)
            .show()
    }

    private fun showTerminateAllDialog() {
        val otherCount = allSessions.count { it.deviceId != globalParam.deviceId }
        if (otherCount == 0) return

        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.devices_terminate_all_title)
            .setMessage(getString(R.string.devices_terminate_all_message, otherCount))
            .setPositiveButton(R.string.devices_terminate_all_action) { _, _ ->
                terminateAllOtherSessions()
            }
            .setNegativeButton(R.string.btn_cancel, null)
            .show()
    }

    private fun removeSession(deviceId: String) {
        lifecycleScope.launch {
            val result = userProfileGateway.removeActiveSession(deviceId)
            if (result.isSuccess) {
                allSessions = allSessions.filter { it.deviceId != deviceId }
                updateUI()
                Toast.makeText(this@DevicesActivity, R.string.device_session_terminated, Toast.LENGTH_SHORT).show()
            } else {
                Toast.makeText(this@DevicesActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
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
                val result = userProfileGateway.removeActiveSession(deviceId)
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
                Toast.makeText(this@DevicesActivity, getString(R.string.devices_all_terminated, successCount), Toast.LENGTH_SHORT).show()
            } else {
                loadSessions()
                Toast.makeText(
                    this@DevicesActivity,
                    getString(R.string.devices_terminated_summary, successCount, failCount),
                    Toast.LENGTH_SHORT
                ).show()
            }
        }
    }
}
