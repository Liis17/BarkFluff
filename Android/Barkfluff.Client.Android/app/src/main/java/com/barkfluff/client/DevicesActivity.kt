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

        // Обработчик клика на текущее устройство
        binding.layoutCurrentDevice.setOnClickListener {
            val currentSession = allSessions.find { it.deviceId == globalParam.deviceId }
            currentSession?.let { showDeviceDetailsBottomSheet(it) }
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
            // Устанавливаем иконку устройства
            binding.imageCurrentDeviceIcon.setImageResource(DeviceAdapter.getDeviceIcon(currentSession))

            // Если есть кастомное имя - показываем его, иначе только оригинальное
            if (currentSession.customName.isNotEmpty()) {
                binding.textCurrentDeviceName.text = currentSession.customName
                binding.textCurrentDeviceOriginalName.text = currentSession.originalName
                binding.textCurrentDeviceOriginalName.visibility = android.view.View.VISIBLE
            } else {
                binding.textCurrentDeviceName.text = currentSession.originalName
                binding.textCurrentDeviceOriginalName.visibility = android.view.View.GONE
            }
            // Показываем appName без версии
            val appNameWithoutVersion = extractAppNameWithoutVersion(currentSession.appName)
            binding.textCurrentDeviceApp.text = appNameWithoutVersion
            binding.textCurrentDeviceApp.visibility = android.view.View.VISIBLE
            binding.cardCurrentDevice.visibility = android.view.View.VISIBLE
        } else {
            binding.cardCurrentDevice.visibility = android.view.View.GONE
        }

        // Обновляем кнопку завершения всех сессий
        binding.buttonTerminateAll.isEnabled = otherSessions.isNotEmpty()

        // Показываем список других устройств
        if (otherSessions.isNotEmpty()) {
            binding.textOtherDevicesTitle.visibility = android.view.View.VISIBLE
            deviceAdapter.submitList(otherSessions)
        } else {
            binding.textOtherDevicesTitle.visibility = android.view.View.GONE
            deviceAdapter.submitList(emptyList())
        }
    }

    /**
     * Извлекает имя приложения без версии (первые 2 слова)
     * Например: "BarkFluff Desktop 1.0.0" -> "BarkFluff Desktop"
     */
    private fun extractAppNameWithoutVersion(appName: String): String {
        val words = appName.split(" ")
        return if (words.size >= 2) {
            // Проверяем, не является ли последнее слово версией (содержит цифры и точки)
            val lastWord = words.last()
            if (lastWord.matches(Regex("^[0-9.]+$"))) {
                // Последнее слово - версия, убираем его
                words.dropLast(1).joinToString(" ")
            } else {
                // Последнее слово не версия, берем первые 2 слова
                words.take(2).joinToString(" ")
            }
        } else {
            appName
        }
    }

    private fun showDeviceDetailsBottomSheet(session: GrpcManager.SessionData) {
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
            sheetBinding.textDeviceTitle.text = session.originalName
            sheetBinding.textDeviceTitle.visibility = android.view.View.VISIBLE
            sheetBinding.textDeviceOriginalName.visibility = android.view.View.GONE
        }

        sheetBinding.textAppName.text = session.appName
        sheetBinding.textOS.text = session.os
        sheetBinding.textLocation.text = session.location.ifEmpty { "Неизвестно" }
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

    private fun showRenameDeviceDialog(session: GrpcManager.SessionData) {
        val builder = MaterialAlertDialogBuilder(this)
        builder.setTitle("Переименовать устройство")

        val input = android.widget.EditText(this)
        input.hint = "Введите новое имя устройства"
        input.setText(session.customName.ifEmpty { session.originalName })
        builder.setView(input)

        builder.setPositiveButton("Сохранить") { _, _ ->
            val newCustomName = input.text.toString().trim()
            if (newCustomName.isNotEmpty()) {
                renameDevice(session.deviceId, newCustomName)
            }
        }
        builder.setNegativeButton("Отмена", null)
        builder.show()
    }

    private fun renameDevice(deviceId: String, customName: String) {
        binding.progressLoading.visibility = View.VISIBLE

        lifecycleScope.launch {
            val result = grpcManager.renameDevice(deviceId, customName)
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
                Toast.makeText(this@DevicesActivity, "Устройство переименовано", Toast.LENGTH_SHORT).show()
            } else {
                Toast.makeText(this@DevicesActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
            }
        }
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
