package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.ServerAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.ServerDataElement
import com.barkfluff.client.databinding.ActivitySelectServerBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.security.TlsCertificateInfo
import com.barkfluff.client.security.TlsCertificateProbe
import com.barkfluff.client.security.TlsTrustStore
import com.barkfluff.client.utils.applyServerInfo
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import java.net.URI
import java.text.DateFormat
import java.util.Date
import kotlin.coroutines.resume

/**
 * Активность выбора сервера
 * Аналог SelectServer.xaml из WPF клиента
 */
class SelectServerActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "SelectServerActivity"
    }

    private lateinit var binding: ActivitySelectServerBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var serverAdapter: ServerAdapter
    private lateinit var tlsTrustStore: TlsTrustStore
    private val certificateProbe = TlsCertificateProbe()

    private var isConnecting = false
    private val pingCache = mutableMapOf<String, Int?>()

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivitySelectServerBinding.inflate(layoutInflater)
        setContentView(binding.root)

        // Инициализация
        globalParam = GlobalParam(this)
        grpcManager = GrpcManager(applicationContext)
        tlsTrustStore = TlsTrustStore(applicationContext)

        // Загружаем внешний IP-адрес асинхронно
        lifecycleScope.launch {
            GlobalParam.loadIpAddress(globalParam.sharedPreferences)
        }

        setupRecyclerView()
        setupClickListeners()
        loadServerList()
    }

    private fun setupRecyclerView() {
        serverAdapter = ServerAdapter(
            coroutineScope = lifecycleScope,
            measurePing = { ip ->
                if (pingCache.containsKey(ip)) pingCache[ip]
                else measureServerPingMs(ip).also { pingCache[ip] = it }
            },
            onServerClick = { server -> onServerSelected(server) }
        )

        binding.serverListRecyclerView.apply {
            layoutManager = LinearLayoutManager(this@SelectServerActivity)
            adapter = serverAdapter
        }
    }

    private suspend fun measureServerPingMs(address: String): Int? = withTimeoutOrNull(3000L) {
        val pingManager = GrpcManager(applicationContext)
        try {
            if (pingManager.createOnlyBeaconClient(address).isFailure) return@withTimeoutOrNull null
            val start = System.currentTimeMillis()
            if (pingManager.getServerInfo().isFailure) return@withTimeoutOrNull null
            (System.currentTimeMillis() - start).toInt()
        } finally {
            pingManager.shutdown()
        }
    }

    private fun setupClickListeners() {
        // «Своя нода» разворачивает поле ручного ввода (макет 2c)
        binding.customServerRow.setOnClickListener { toggleCustomServerPanel() }

        // Кнопка подключения
        binding.connectButton.setOnClickListener {
            val address = binding.serverAddressEditText.text.toString().trim()
            normalizeServerAddress(address)?.let(::connectToServer)
        }
        binding.forgetTrustedCertificateButton.setOnClickListener {
            val address = binding.serverAddressEditText.text.toString().trim()
            val normalized = normalizeServerAddress(address) ?: return@setOnClickListener
            val host = URI(normalized).host ?: return@setOnClickListener
            tlsTrustStore.removePin(host)
            MaterialAlertDialogBuilder(this)
                .setMessage(getString(R.string.tls_certificate_forgotten, host))
                .setPositiveButton(android.R.string.ok, null)
                .show()
        }
    }

    private fun toggleCustomServerPanel() {
        val expanded = binding.customServerPanel.visibility != View.VISIBLE
        binding.customServerPanel.visibility = if (expanded) View.VISIBLE else View.GONE
        binding.customServerChevron.animate()
            .rotation(if (expanded) 180f else 0f)
            .setDuration(180L)
            .start()
    }

    private fun loadServerList() {
        showLoading(true)
        pingCache.clear()

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
                                description = "Публичная нода для тестирования",
                                userCount = "125",
                                publicName = "barkfluff-public-1",
                                location = "Москва, RU",
                                hexColor = "#FF6B35"
                            ),
                            ServerDataElement(
                                ip = "test2.barkfluff.com:64646",
                                title = "BarkFluff Public Server 2",
                                description = "Вторая публичная нода",
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
                    showError(result.exceptionOrNull()?.message ?: "Не удалось загрузить список нод")
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
        normalizeServerAddress(server.ip)?.let(::connectToServer)
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
                // Создаем Beacon клиент
                val createResult = grpcManager.createOnlyBeaconClient(address)
                if (createResult.isFailure) {
                    showError(createResult.exceptionOrNull()?.message ?: "Не удалось подключиться к ноде")
                    resetConnectionState()
                    return@launch
                }

                // Получаем информацию о сервере. Для self-signed Beacon сперва показываем
                // fingerprint, а адрес сохраняем только после завершения trust flow.
                var infoResult = grpcManager.getServerInfo()
                if (infoResult.isFailure && approveCertificateIfEligible(address)) {
                    infoResult = grpcManager.getServerInfo()
                }

                if (infoResult.isSuccess) {
                    val serverInfo = infoResult.getOrNull()
                    if (serverInfo != null) {
                        if (!preflightServerCertificates(serverInfo)) {
                            resetConnectionState()
                            return@launch
                        }

                        // Сохраняем информацию о сервере в GlobalParam
                        globalParam.socketBeacon = address
                        globalParam.applyServerInfo(serverInfo)

                        Log.d(TAG, "Успешное подключение к серверу: ${serverInfo.name}")

                        // Создаем Identity клиент для проверки доступности (без interceptor, так как токена еще нет)
                        val identityResult = grpcManager.createIdentityClient(globalParam.socketIdentity)
                        if (identityResult.isFailure) {
                            Log.e(TAG, "Не удалось создать Identity клиент")
                        }

                        // Переход на главный экран
                        openMainActivity()
                    } else {
                        showError("Не удалось получить информацию о ноде")
                        resetConnectionState()
                    }
                } else {
                    showError(infoResult.exceptionOrNull()?.message ?: "Не удалось получить информацию о ноде")
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

    private fun normalizeServerAddress(input: String): String? = runCatching {
        grpcManager.normalizeEndpointAddress(input)
    }.onFailure {
        showError(getString(R.string.tls_invalid_endpoint))
    }.getOrNull()

    private suspend fun preflightServerCertificates(serverInfo: GrpcManager.ServerInfo): Boolean {
        val addresses = listOf(
            serverInfo.identityEndpoint,
            serverInfo.usersEndpoint,
            serverInfo.filesEndpoint,
            serverInfo.messagesEndpoint,
            serverInfo.updatesEndpoint,
            serverInfo.onlinerEndpoint,
            serverInfo.fastAuthEndpoint,
            serverInfo.callsEndpoint,
            serverInfo.livekitUrl
        ).filter { it.isNotBlank() }

        val inspectedHosts = mutableSetOf<String>()
        for (address in addresses) {
            val certificate = withContext(Dispatchers.IO) {
                runCatching {
                    if (address.startsWith("wss://", ignoreCase = true)) {
                        certificateProbe.inspectUrl(address)
                    } else {
                        certificateProbe.inspect(address)
                    }
                }.getOrNull()
            } ?: continue
            if (!inspectedHosts.add(certificate.host)) continue
            val existingPin = tlsTrustStore.pinFor(certificate.host)
            if (existingPin?.spkiSha256 == certificate.spkiSha256) continue
            if (existingPin == null && !certificate.isSelfSigned) continue
            if (!approveCertificateIfEligible(certificate)) return false
        }
        return true
    }

    private suspend fun approveCertificateIfEligible(address: String): Boolean {
        val certificate = withContext(Dispatchers.IO) {
            runCatching { certificateProbe.inspect(address) }.getOrNull()
        } ?: return false
        return approveCertificateIfEligible(certificate)
    }

    private suspend fun approveCertificateIfEligible(certificate: TlsCertificateInfo): Boolean {
        val existingPin = tlsTrustStore.pinFor(certificate.host)
        if (existingPin?.spkiSha256 == certificate.spkiSha256) return true
        if (existingPin == null && !certificate.isSelfSigned) return false

        return suspendCancellableCoroutine { continuation ->
            val expiry = DateFormat.getDateTimeInstance().format(Date(certificate.expiresAtMillis))
            val message = if (existingPin == null) {
                getString(
                    R.string.tls_self_signed_message,
                    certificate.host,
                    certificate.subject,
                    expiry,
                    certificate.spkiSha256
                )
            } else {
                getString(
                    R.string.tls_changed_pin_message,
                    certificate.host,
                    existingPin.spkiSha256,
                    certificate.spkiSha256,
                    certificate.subject,
                    expiry
                )
            }
            val dialog = MaterialAlertDialogBuilder(this)
                .setTitle(
                    if (existingPin == null) R.string.tls_self_signed_title else R.string.tls_changed_pin_title
                )
                .setMessage(message)
                .setNegativeButton(R.string.tls_cancel) { _, _ ->
                    if (continuation.isActive) continuation.resume(false)
                }
                .setPositiveButton(R.string.tls_trust_certificate) { _, _ ->
                    tlsTrustStore.replacePin(certificate.host, certificate.spkiSha256)
                    if (continuation.isActive) continuation.resume(true)
                }
                .create()
            dialog.setOnShowListener {
                dialog.findViewById<TextView>(android.R.id.message)?.setTextIsSelectable(true)
            }
            dialog.setOnCancelListener {
                if (continuation.isActive) continuation.resume(false)
            }
            continuation.invokeOnCancellation { dialog.dismiss() }
            dialog.show()
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
