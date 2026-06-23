package com.barkfluff.client

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.view.View
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.ServerAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.ServerDataElement
import com.barkfluff.client.databinding.ActivitySelectServerBinding
import com.barkfluff.client.grpc.GrpcManager
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.launch
import java.util.regex.Pattern

/**
 * Активность выбора сервера
 * Аналог SelectServer.xaml из WPF клиента
 */
class SelectServerActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "SelectServerActivity"
        private const val MIN_PORT = 1
        private const val MAX_PORT = 65535
        private val SERVER_ADDRESS_PATTERN = Pattern.compile("^([^:]+):(\\d+)$")
    }

    private lateinit var binding: ActivitySelectServerBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var serverAdapter: ServerAdapter

    private var isConnecting = false

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivitySelectServerBinding.inflate(layoutInflater)
        setContentView(binding.root)

        // Инициализация
        globalParam = GlobalParam(this)
        grpcManager = GrpcManager()

        // Загружаем внешний IP-адрес асинхронно
        lifecycleScope.launch {
            GlobalParam.loadIpAddress(globalParam.sharedPreferences)
        }

        setupRecyclerView()
        setupClickListeners()
        loadServerList()
    }

    private fun setupRecyclerView() {
        serverAdapter = ServerAdapter { server ->
            onServerSelected(server)
        }

        binding.serverListRecyclerView.apply {
            layoutManager = LinearLayoutManager(this@SelectServerActivity)
            adapter = serverAdapter
        }
    }

    private fun setupClickListeners() {
        // Кнопка подключения
        binding.connectButton.setOnClickListener {
            val address = binding.serverAddressEditText.text.toString().trim()
            if (validateServerAddress(address)) {
                connectToServer(address)
            }
        }
    }

    private fun loadServerList() {
        showLoading(true)

        lifecycleScope.launch {
            try {
                // Создаем Navigator клиент
                val createResult = grpcManager.createNavigatorClient()
                if (createResult.isFailure) {
                    showError(createResult.exceptionOrNull()?.message ?: "Не удалось создать соединение с навигатором")
                    return@launch
                }

                // Получаем список серверов
                val result = grpcManager.getServerList()

                if (result.isSuccess) {
                    val servers = result.getOrNull()
                    if (servers.isNullOrEmpty()) {
                        // Показываем тестовые данные если список пуст
                        val testServers = listOf(
                            ServerDataElement(
                                ip = "test1.barkfluff.com:64646",
                                title = "BarkFluff Public Server 1",
                                description = "Публичный сервер для тестирования",
                                userCount = "125",
                                publicName = "barkfluff-public-1",
                                location = "Москва, RU",
                                hexColor = "#FF6B35"
                            ),
                            ServerDataElement(
                                ip = "test2.barkfluff.com:64646",
                                title = "BarkFluff Public Server 2",
                                description = "Второй публичный сервер",
                                userCount = "89",
                                publicName = "barkfluff-public-2",
                                location = "Санкт-Петербург, RU",
                                hexColor = "#2196F3"
                            )
                        )
                        serverAdapter.submitList(testServers)
                    } else {
                        serverAdapter.submitList(servers)
                        Log.d(TAG, "Загружено ${servers.size} серверов")
                    }
                } else {
                    showError(result.exceptionOrNull()?.message ?: "Не удалось загрузить список серверов")
                    Log.e(TAG, "Ошибка загрузки списка серверов", result.exceptionOrNull())
                }
            } catch (e: Exception) {
                showError("Ошибка: ${e.message}")
                Log.e(TAG, "Ошибка загрузки списка серверов", e)
            } finally {
                showLoading(false)
            }
        }
    }

    private fun onServerSelected(server: ServerDataElement) {
        Log.d(TAG, "Выбран сервер: ${server.title} (${server.ip})")
        connectToServer(server.ip)
    }

    private fun connectToServer(address: String) {
        if (isConnecting) {
            return
        }

        isConnecting = true
        binding.connectButton.isEnabled = false
        showLoading(true)

        lifecycleScope.launch {
            try {
                // Сохраняем адрес beacon
                globalParam.socketBeacon = address

                // Создаем Beacon клиент
                val createResult = grpcManager.createOnlyBeaconClient(address)
                if (createResult.isFailure) {
                    showError(createResult.exceptionOrNull()?.message ?: "Не удалось подключиться к серверу")
                    resetConnectionState()
                    return@launch
                }

                // Получаем информацию о сервере
                val infoResult = grpcManager.getServerInfo()

                if (infoResult.isSuccess) {
                    val serverInfo = infoResult.getOrNull()
                    if (serverInfo != null) {
                        // Сохраняем информацию о сервере в GlobalParam
                        globalParam.apply {
                            serverName = serverInfo.name
                            serverDescription = serverInfo.description
                            socketIdentity = ensureHttpPrefix(serverInfo.identityEndpoint)
                            socketUsers = ensureHttpPrefix(serverInfo.usersEndpoint)
                            socketFiles = ensureHttpPrefix(serverInfo.filesEndpoint)
                            socketMessages = ensureHttpPrefix(serverInfo.messagesEndpoint)
                            socketUpdates = ensureHttpPrefix(serverInfo.updatesEndpoint)
                            socketOnliner = ensureHttpPrefix(serverInfo.onlinerEndpoint)
                            socketFastAuth = ensureHttpPrefix(serverInfo.fastAuthEndpoint)
                            socketCalls = ensureHttpPrefix(serverInfo.callsEndpoint)
                            livekitUrl = serverInfo.livekitUrl
                            colors = serverInfo.color
                        }

                        Log.d(TAG, "Успешное подключение к серверу: ${serverInfo.name}")

                        // Создаем Identity клиент для проверки доступности (без interceptor, так как токена еще нет)
                        val identityResult = grpcManager.createIdentityClient(globalParam.socketIdentity)
                        if (identityResult.isFailure) {
                            Log.e(TAG, "Не удалось создать Identity клиент")
                        }

                        // Переход на главный экран
                        openMainActivity()
                    } else {
                        showError("Не удалось получить информацию о сервере")
                        resetConnectionState()
                    }
                } else {
                    showError(infoResult.exceptionOrNull()?.message ?: "Не удалось получить информацию о сервере")
                    Log.e(TAG, "Ошибка получения информации о сервере", infoResult.exceptionOrNull())
                    resetConnectionState()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка подключения к серверу", e)
                showError("Ошибка подключения: ${e.message}")
                resetConnectionState()
            }
        }
    }

    private fun resetConnectionState() {
        isConnecting = false
        binding.connectButton.isEnabled = true
        showLoading(false)
    }

    private fun validateServerAddress(input: String): Boolean {
        val trimmedInput = input.trim()

        if (trimmedInput.isBlank()) {
            showError("Укажите адрес сервера")
            return false
        }

        val match = SERVER_ADDRESS_PATTERN.matcher(trimmedInput)
        if (!match.matches()) {
            showError("Используйте формат: адрес:порт")
            return false
        }

        val host = match.group(1).trim()
        val portStr = match.group(2)

        val port = portStr.toIntOrNull()
        if (port == null || port < MIN_PORT || port > MAX_PORT) {
            showError("Порт должен быть числом от $MIN_PORT до $MAX_PORT")
            return false
        }

        if (!isValidHost(host)) {
            showError("Некорректный адрес сервера")
            return false
        }

        return true
    }

    private fun isValidHost(host: String): Boolean {
        if (host.isBlank()) {
            return false
        }

        // Проверяем IP адрес
        try {
            val parts = host.split(".")
            if (parts.size == 4) {
                parts.forEach { part ->
                    val num = part.toIntOrNull() ?: return@forEach
                    if (num < 0 || num > 255) {
                        return false
                    }
                }
                return true
            }
        } catch (e: Exception) {
            // Не IP адрес
        }

        // Проверяем DNS имя
        return try {
            val uri = Uri.parse("http://$host")
            uri.host != null
        } catch (e: Exception) {
            false
        }
    }

    private fun ensureHttpPrefix(url: String): String {
        if (url.isBlank()) {
            return url
        }

        return if (!url.startsWith("http://") && !url.startsWith("https://")) {
            "https://$url"
        } else {
            url
        }
    }

    private fun showLoading(isLoading: Boolean) {
        binding.loadingProgressBar.visibility = if (isLoading) View.VISIBLE else View.GONE
        if (isLoading) {
            binding.serverListRecyclerView.visibility = View.GONE
        } else {
            binding.serverListRecyclerView.visibility = View.VISIBLE
        }
    }

    private fun showError(message: String) {
        runOnUiThread {
            MaterialAlertDialogBuilder(this)
                .setTitle("Ошибка")
                .setMessage(message)
                .setPositiveButton("OK", null)
                .show()
        }
    }

    private fun openMainActivity() {
        val intent = Intent(this, LoginActivity::class.java)
        startActivity(intent)
        finish()
    }

    override fun onBackPressed() {
        // Блокируем возврат на предыдущий экран
        MaterialAlertDialogBuilder(this)
            .setTitle("Выход из приложения")
            .setMessage("Вы действительно хотите выйти?")
            .setPositiveButton("Выйти") { _, _ ->
                super.onBackPressed()
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    override fun onDestroy() {
        super.onDestroy()
        grpcManager.shutdown()
    }
}
