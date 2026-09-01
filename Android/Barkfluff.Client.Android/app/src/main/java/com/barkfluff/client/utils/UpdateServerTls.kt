package com.barkfluff.client.utils

import android.util.Base64
import android.util.Log
import com.barkfluff.client.BuildConfig
import kotlinx.coroutines.CancellationException
import java.io.ByteArrayInputStream
import java.security.KeyStore
import java.security.cert.CertificateException
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLException
import javax.net.ssl.SSLSocketFactory
import javax.net.ssl.TrustManagerFactory
import javax.net.ssl.X509TrustManager

/**
 * Доверие CA сервера обновлений.
 *
 * CA для старого TLS-сценария storage.barkfluff.com приезжает в APK строкой
 * [BuildConfig.STORAGE_CA_PEM_B64] — её заполняет воркфлоу из секрета
 * CLOUDFLARE_ORIGIN_CA_BUNDLE_B64, того же, которым он ходит на storage через
 * `curl --cacert`. Он используется только как fallback после неудачной проверки через
 * системное хранилище Android.
 *
 * Доверие расширяется точечно, только для клиентов системы обновлений. `network_security_config`
 * намеренно не трогаем: появление там любого `domain-config` ломает [
 * com.barkfluff.client.security.PinnedTrustManager] — он делегирует в hostname-неосведомлённый
 * `checkServerTrusted(chain, authType)`, а платформенный RootTrustManager на такой вызов при
 * наличии per-domain конфигураций бросает CertificateException, то есть отвалился бы весь
 * остальной трафик приложения.
 *
 * Если сертификат не задан (локальная сборка без переменной окружения), fallback недоступен,
 * но первая попытка всё равно работает через платформенное хранилище.
 */
object UpdateServerTls {

    private const val TAG = "UpdateServerTls"

    data class Trust(
        val socketFactory: SSLSocketFactory,
        val trustManager: X509TrustManager
    )

    val trust: Trust? by lazy { createTrust() }

    /**
     * Выполняет операцию сначала с системным trust store. Embedded CA разрешается лениво и
     * используется только после TLS-ошибки. [operation] получает null для первой попытки и
     * подготовленный [Trust] для fallback.
     */
    internal suspend fun <T> withFallback(
        operation: suspend (Trust?) -> T
    ): T = withFallback(
        embeddedTrustProvider = { trust },
        operation = operation
    )

    /**
     * Тестируемая форма [withFallback]. Provider нужен, чтобы тесты не зависели от BuildConfig,
     * а production-путь не разбирал сертификат до первой TLS-ошибки.
     */
    internal suspend fun <T> withFallback(
        embeddedTrustProvider: () -> Trust?,
        operation: suspend (Trust?) -> T
    ): T {
        try {
            return operation(null)
        } catch (error: Exception) {
            if (error is CancellationException || !isTlsFailure(error)) {
                throw error
            }

            val embeddedTrust = embeddedTrustProvider() ?: throw error
            return operation(embeddedTrust)
        }
    }

    private fun isTlsFailure(error: Throwable): Boolean {
        var current: Throwable? = error
        while (current != null) {
            if (current is SSLException || current is CertificateException) {
                return true
            }
            current = current.cause
        }
        return false
    }

    private fun createTrust(): Trust? {
        val encoded = BuildConfig.STORAGE_CA_PEM_B64
        if (encoded.isEmpty()) {
            Log.i(TAG, "CA сервера обновлений не задан — используется системное хранилище")
            return null
        }

        return try {
            val pem = Base64.decode(encoded, Base64.DEFAULT)
            val certificates = CertificateFactory.getInstance("X.509")
                .generateCertificates(ByteArrayInputStream(pem))
                .filterIsInstance<X509Certificate>()

            if (certificates.isEmpty()) {
                Log.w(TAG, "В STORAGE_CA_PEM_B64 нет ни одного X.509-сертификата")
                return null
            }

            val keyStore = KeyStore.getInstance(KeyStore.getDefaultType()).apply {
                load(null, null)
                certificates.forEachIndexed { index, certificate ->
                    setCertificateEntry("storage-ca-$index", certificate)
                }
            }

            val factory = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm())
            factory.init(keyStore)
            val trustManager = factory.trustManagers.filterIsInstance<X509TrustManager>().first()

            val sslContext = SSLContext.getInstance("TLS")
            sslContext.init(null, arrayOf(trustManager), null)

            Trust(sslContext.socketFactory, trustManager)
        } catch (e: Exception) {
            Log.e(TAG, "Не удалось разобрать CA сервера обновлений", e)
            null
        }
    }
}
