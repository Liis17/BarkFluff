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
import com.barkfluff.client.domain.gateway.AuthGateway
import com.barkfluff.client.domain.gateway.ServerDiscoveryGateway
import com.barkfluff.client.domain.model.ServerInfo
import com.barkfluff.client.security.TlsCertificateInfo
import com.barkfluff.client.security.TlsCertificateProbe
import com.barkfluff.client.security.TlsTrustStore
import com.barkfluff.client.utils.TlsEndpointSecurityException
import com.barkfluff.client.utils.TlsServerCertificatePreflight
import com.barkfluff.client.utils.applyServerInfo
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import dagger.hilt.android.AndroidEntryPoint
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
@AndroidEntryPoint
class SelectServerActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "SelectServerActivity"
        const val EXTRA_TRUST_REVIEW_ADDRESS = "tls_trust_review_address"
    }

    private lateinit var binding: ActivitySelectServerBinding
    private lateinit var globalParam: GlobalParam
    @javax.inject.Inject lateinit var serverDiscoveryGateway: ServerDiscoveryGateway
    @javax.inject.Inject lateinit var authGateway: AuthGateway
    private lateinit var serverAdapter: ServerAdapter
    private lateinit var tlsTrustStore: TlsTrustStore
    private val certificateProbe = TlsCertificateProbe()
    private lateinit var certificatePreflight: TlsServerCertificatePreflight

    private var isConnecting = false
    private val pingCache = mutableMapOf<String, Int?>()
    private var currentServerListState = ServerListState.LOADING

    private enum class ServerListState {
        LOADING,
        CONTENT,
        EMPTY,
        ERROR,
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivitySelectServerBinding.inflate(layoutInflater)
        setContentView(binding.root)
        updateCustomServerAccessibility(expanded = false)

        // Инициализация
        globalParam = GlobalParam(this)
        tlsTrustStore = TlsTrustStore(applicationContext)
        certificatePreflight = TlsServerCertificatePreflight(tlsTrustStore, certificateProbe)

        // Загружаем внешний IP-адрес асинхронно
        lifecycleScope.launch {
            GlobalParam.loadIpAddress(globalParam.sharedPreferences)
        }

        setupRecyclerView()
        setupClickListeners()
        loadServerList()

        intent.getStringExtra(EXTRA_TRUST_REVIEW_ADDRESS)
            ?.let(::normalizeServerAddress)
            ?.let { address ->
                binding.serverAddressEditText.setText(address)
                connectToServer(address)
            }
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
        val start = System.currentTimeMillis()
        if (serverDiscoveryGateway.probe(address).isFailure) return@withTimeoutOrNull null
        (System.currentTimeMillis() - start).toInt()
    }

    private fun setupClickListeners() {
        binding.serverListRetryButton.setOnClickListener { loadServerList() }

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
        updateCustomServerAccessibility(expanded)
        binding.customServerChevron.animate()
            .rotation(if (expanded) 180f else 0f)
            .setDuration(180L)
            .start()
    }

    private fun updateCustomServerAccessibility(expanded: Boolean) {
        binding.customServerRow.stateDescription = getString(
            if (expanded) {
                R.string.server_custom_row_expanded
            } else {
                R.string.server_custom_row_collapsed
            }
        )
    }

    private fun loadServerList() {
        renderServerListState(ServerListState.LOADING)
        pingCache.clear()

        lifecycleScope.launch {
            try {
                // Создаем Navigator клиент
                val createResult = serverDiscoveryGateway.createNavigator()
                if (createResult.isFailure) {
                    serverAdapter.submitList(emptyList())
                    renderServerListState(ServerListState.ERROR)
                    Log.e(
                        TAG,
                        "Ошибка подключения к каталогу Navigator",
                        createResult.exceptionOrNull()
                    )
                    return@launch
                }

                // Получаем список серверов
                val result = serverDiscoveryGateway.listServers()

                if (result.isSuccess) {
                    val servers = result.getOrNull()
                    if (servers.isNullOrEmpty()) {
                        serverAdapter.submitList(emptyList())
                        renderServerListState(ServerListState.EMPTY)
                    } else {
                        serverAdapter.submitList(servers)
                        renderServerListState(ServerListState.CONTENT)
                        Log.d(TAG, "Загружено ${servers.size} серверов")
                    }
                } else {
                    serverAdapter.submitList(emptyList())
                    renderServerListState(ServerListState.ERROR)
                    Log.e(
                        TAG,
                        "Ошибка загрузки списка серверов",
                        result.exceptionOrNull()
                    )
                }
            } catch (e: Exception) {
                serverAdapter.submitList(emptyList())
                renderServerListState(ServerListState.ERROR)
                Log.e(TAG, "Ошибка загрузки списка серверов", e)
            }
        }
    }

    private fun onServerSelected(server: ServerDataElement) {
        Log.d(TAG, "Выбран сервер: ${server.title} (${server.ip})")
        normalizeServerAddress(server.ip)?.let { address ->
            connectToServer(address, server.filesMediaEndpoint)
        }
    }

    private fun connectToServer(address: String, navigatorFilesMediaEndpoint: String = "") {
        if (isConnecting) {
            return
        }

        isConnecting = true
        binding.connectButton.isEnabled = false
        showLoading(true)

        lifecycleScope.launch {
            try {
                // Создаем Beacon клиент
                val createResult = serverDiscoveryGateway.createBeacon(address)
                if (createResult.isFailure) {
                    showError(
                        createResult.exceptionOrNull()?.message
                            ?: getString(R.string.select_server_connection_failed)
                    )
                    resetConnectionState()
                    return@launch
                }

                // Получаем информацию о сервере. Для self-signed Beacon сперва показываем
                // fingerprint, а адрес сохраняем только после завершения trust flow.
                var infoResult = serverDiscoveryGateway.serverInfo()
                if (infoResult.isFailure && approveCertificateIfEligible(address)) {
                    infoResult = serverDiscoveryGateway.serverInfo()
                }

                if (infoResult.isSuccess) {
                    val serverInfo = infoResult.getOrNull()
                    if (serverInfo != null) {
                        val effectiveServerInfo = if (navigatorFilesMediaEndpoint.isBlank()) {
                            serverInfo
                        } else {
                            serverInfo.copy(filesMediaEndpoint = navigatorFilesMediaEndpoint)
                        }
                        if (!preflightServerCertificates(effectiveServerInfo)) {
                            resetConnectionState()
                            return@launch
                        }

                        // Сохраняем информацию о сервере в GlobalParam
                        globalParam.socketBeacon = address
                        globalParam.applyServerInfo(effectiveServerInfo)

                        Log.d(TAG, "Успешное подключение к серверу: ${effectiveServerInfo.name}")

                        // Создаем Identity клиент для проверки доступности (без interceptor, так как токена еще нет)
                        val identityResult = authGateway.createIdentity(globalParam.socketIdentity)
                        if (identityResult.isFailure) {
                            Log.e(TAG, "Не удалось создать Identity клиент")
                        }

                        // Переход на главный экран
                        openMainActivity()
                    } else {
                        showError(getString(R.string.select_server_info_failed))
                        resetConnectionState()
                    }
                } else {
                    showError(
                        infoResult.exceptionOrNull()?.message
                            ?: getString(R.string.select_server_info_failed)
                    )
                    Log.e(TAG, "Ошибка получения информации о сервере", infoResult.exceptionOrNull())
                    resetConnectionState()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка подключения к серверу", e)
                showError(getString(R.string.settings_error_detail, e.message.orEmpty()))
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
        serverDiscoveryGateway.normalizeEndpoint(input)
    }.onFailure {
        showError(getString(R.string.tls_invalid_endpoint))
    }.getOrNull()

    private suspend fun preflightServerCertificates(serverInfo: ServerInfo): Boolean {
        while (true) {
            val certificate = try {
                withContext(Dispatchers.IO) {
                    certificatePreflight.approvalRequired(serverInfo)
                }
            } catch (error: TlsEndpointSecurityException) {
                showError(getString(R.string.tls_certificate_invalid))
                return false
            } ?: return true
            if (!approveCertificateIfEligible(certificate)) return false
        }
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


    private fun renderServerListState(state: ServerListState) {
        currentServerListState = state
        binding.loadingProgressBar.visibility = if (state == ServerListState.LOADING) {
            View.VISIBLE
        } else {
            View.GONE
        }
        binding.serverListRecyclerView.visibility = if (state == ServerListState.CONTENT) {
            View.VISIBLE
        } else {
            View.GONE
        }

        val showMessage = state == ServerListState.EMPTY || state == ServerListState.ERROR
        binding.serverListState.visibility = if (showMessage) View.VISIBLE else View.GONE
        if (showMessage) {
            binding.serverListStateTitle.setText(
                if (state == ServerListState.EMPTY) {
                    R.string.server_list_empty_title
                } else {
                    R.string.server_list_error_title
                }
            )
            binding.serverListStateMessage.setText(
                if (state == ServerListState.EMPTY) {
                    R.string.server_list_empty_message
                } else {
                    R.string.server_list_error_message
                }
            )
        }
    }

    private fun showLoading(isLoading: Boolean) {
        if (isLoading) {
            binding.loadingProgressBar.visibility = View.VISIBLE
            binding.serverListRecyclerView.visibility = View.GONE
            binding.serverListState.visibility = View.GONE
        } else {
            renderServerListState(currentServerListState)
        }
    }

    private fun showError(message: String) {
        runOnUiThread {
            MaterialAlertDialogBuilder(this)
                .setTitle(R.string.error)
                .setMessage(message)
                .setPositiveButton(R.string.dialog_ok, null)
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
            .setTitle(R.string.logout_title)
            .setMessage(R.string.logout_message)
            .setPositiveButton(R.string.logout_action) { _, _ ->
                super.onBackPressed()
            }
            .setNegativeButton(R.string.btn_cancel, null)
            .show()
    }

    override fun onDestroy() {
        super.onDestroy()
    }
}
