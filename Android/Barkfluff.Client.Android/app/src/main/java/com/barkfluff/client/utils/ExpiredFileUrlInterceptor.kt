package com.barkfluff.client.utils

import coil.intercept.Interceptor
import coil.network.HttpException
import coil.request.ErrorResult
import coil.request.ImageResult
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.util.concurrent.ConcurrentHashMap

/**
 * Временная ссылка Files живёт ограниченное время (`TempFiles:ExpiresAt`), а URL-кеш клиента
 * персистентный. Истёкшая ссылка отвечает 404: тогда запрашиваем новую ссылку для того же fileId
 * и один раз повторяем загрузку. Ссылки не из URL-кеша не трогаем.
 */
internal class ExpiredFileUrlInterceptor(
    private val refresher: ExpiredFileUrlRefresher,
) : Interceptor {

    override suspend fun intercept(chain: Interceptor.Chain): ImageResult {
        val result = chain.proceed(chain.request)
        if (result !is ErrorResult || !result.throwable.isHttpNotFound()) return result
        val expiredUrl = chain.request.data as? String ?: return result
        val freshUrl = refresher.refresh(expiredUrl) ?: return result
        return chain.proceed(chain.request.newBuilder().data(freshUrl).build())
    }
}

internal fun Throwable.isHttpNotFound(): Boolean = (this as? HttpException)?.response?.code == 404

internal class ExpiredFileUrlRefresher(
    private val fileIdForUrl: (url: String) -> String?,
    private val currentUrl: (fileId: String) -> String?,
    private val fetchUrl: suspend (fileId: String) -> String?,
    private val onRefreshed: suspend (fileId: String, expiredUrl: String, freshUrl: String?) -> Unit,
) {
    private val locks = ConcurrentHashMap<String, Mutex>()

    // Запросы, начатые со старой ссылкой, находят свой fileId и после её замены в кеше.
    private val expiredFileIds = ConcurrentHashMap<String, String>()

    /** Новая ссылка взамен [expiredUrl] или null, если fileId неизвестен или ссылку получить не удалось. */
    suspend fun refresh(expiredUrl: String): String? {
        val fileId = expiredFileIds[expiredUrl] ?: fileIdForUrl(expiredUrl) ?: return null
        return locks.getOrPut(fileId) { Mutex() }.withLock {
            // Параллельный запрос уже получил новую ссылку — второй раз в Files не идём.
            currentUrl(fileId)?.takeIf { it != expiredUrl }?.let { return@withLock it }
            expiredFileIds[expiredUrl] = fileId
            val freshUrl = try {
                fetchUrl(fileId)?.takeIf { it.isNotBlank() && it != expiredUrl }
            } catch (e: CancellationException) {
                throw e
            } catch (_: Exception) {
                null
            }
            onRefreshed(fileId, expiredUrl, freshUrl)
            freshUrl
        }
    }

    fun clear() = expiredFileIds.clear()
}
