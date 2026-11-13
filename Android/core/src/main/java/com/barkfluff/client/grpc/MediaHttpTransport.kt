package com.barkfluff.client.grpc

import android.content.Context
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.security.TlsTransportFactory
import com.barkfluff.client.utils.FileMediaUrl
import okhttp3.Call
import okhttp3.Callback
import okhttp3.HttpUrl
import okhttp3.OkHttpClient
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.Response
import okhttp3.MediaType
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.RequestBody.Companion.toRequestBody
import okio.BufferedSink
import java.io.FileNotFoundException
import java.io.IOException
import java.io.InputStream
import java.io.InterruptedIOException
import java.io.OutputStream
import java.io.PipedInputStream
import java.io.PipedOutputStream
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

/**
 * HTTP boundary for Files URLs. The compatibility connection below uses OkHttp internally so
 * BFT virtual sockets work while existing callers retain their upload/download behavior.
 */
class MediaHttpTransport(
    context: Context,
    private val tlsTransport: TlsTransportFactory = TlsTransportFactory(context.applicationContext),
    private val mediaOrigin: () -> String = { GlobalParam(context.applicationContext).socketFilesMedia },
) {
    private val clients = mutableMapOf<String, OkHttpClient>()
    private val clientsLock = Any()
    @Volatile private var clientsGeneration = tlsTransport.bftProxyManager?.transportGeneration ?: 0L
    private val generationListener = tlsTransport.bftProxyManager?.addTransportGenerationListener { generation ->
        synchronized(clientsLock) {
            val currentGeneration = tlsTransport.bftProxyManager?.transportGeneration ?: generation
            if (clientsGeneration != currentGeneration) {
                closeClientsLocked()
                clientsGeneration = currentGeneration
            }
        }
    }

    /** Rewrites a Files URL to the node's dedicated media origin when one is configured. */
    fun rewrite(url: String): String = FileMediaUrl.rewrite(url, mediaOrigin())

    /** Opens a rewritten URL through the shared OkHttp TLS and BFT transport policy. */
    fun openConnection(url: String): HttpURLConnection {
        val rewritten = rewrite(url)
        val httpUrl = rewritten.toHttpUrlOrNull() ?: throw IllegalArgumentException("Invalid media URL")
        return OkHttpHttpURLConnection(URL(rewritten), clientFor(httpUrl))
    }

    /** Applies the legacy direct TLS policy to externally-created connections. */
    fun configure(connection: HttpURLConnection) {
        if (connection !is OkHttpHttpURLConnection) tlsTransport.configure(connection)
    }

    private fun clientFor(url: HttpUrl): OkHttpClient {
        synchronized(clientsLock) {
            val generation = tlsTransport.bftProxyManager?.transportGeneration ?: 0L
            if (generation != clientsGeneration) {
                closeClientsLocked()
                clientsGeneration = generation
            }
            val key = "${url.scheme}://${url.host}:${url.port}/files-media"
            return clients[key] ?: tlsTransport.createOkHttpClient(url) {
                protocols(listOf(Protocol.HTTP_1_1))
                retryOnConnectionFailure(false)
            }.also { clients[key] = it }
        }
    }

    private fun closeClientsLocked() {
        clients.values.forEach { client ->
            client.dispatcher.cancelAll()
            client.connectionPool.evictAll()
            client.dispatcher.executorService.shutdown()
        }
        clients.clear()
    }
}

/** HttpURLConnection-shaped adapter for OkHttp, with request bodies streamed through a pipe. */
private class OkHttpHttpURLConnection(
    url: URL,
    private val baseClient: OkHttpClient
) : HttpURLConnection(url) {
    private val requestHeaders = linkedMapOf<String, String>()
    private val responseReady = CountDownLatch(1)
    private var outputPipe: PipedOutputStream? = null
    private var inputPipe: PipedInputStream? = null
    private var fixedLength: Long? = null
    private var call: Call? = null
    @Volatile private var response: Response? = null
    @Volatile private var failure: IOException? = null

    override fun connect() {
        startCall()
    }

    override fun disconnect() {
        call?.cancel()
        runCatching { outputPipe?.close() }
        response?.close()
    }

    override fun usingProxy(): Boolean = false

    override fun getOutputStream(): OutputStream {
        checkNotConnected()
        if (method == "GET") method = "POST"
        if (outputPipe == null) {
            val input = PipedInputStream(PIPE_BUFFER_BYTES)
            inputPipe = input
            outputPipe = PipedOutputStream(input)
        }
        startCall()
        return outputPipe!!
    }

    override fun getInputStream(): InputStream {
        val result = awaitResponse()
        if (!result.isSuccessful) throw FileNotFoundException("HTTP ${result.code}")
        return result.body?.byteStream() ?: InputStream.nullInputStream()
    }

    override fun getErrorStream(): InputStream? {
        val result = runCatching { awaitResponse() }.getOrNull() ?: return null
        return if (result.isSuccessful) null else result.body?.byteStream()
    }

    override fun getResponseCode(): Int = awaitResponse().code

    override fun getResponseMessage(): String? = awaitResponse().message

    override fun getContentLength(): Int = getContentLengthLong().coerceAtMost(Int.MAX_VALUE.toLong()).toInt()

    override fun getContentLengthLong(): Long = awaitResponse().body?.contentLength() ?: -1L

    override fun getHeaderField(name: String?): String? = if (name == null) null else awaitResponse().header(name)

    override fun getHeaderFields(): Map<String, List<String>> = awaitResponse().headers.toMultimap()

    override fun getHeaderFieldKey(n: Int): String? = indexedHeader(n)?.first

    override fun getHeaderField(n: Int): String? = indexedHeader(n)?.second

    override fun getRequestProperty(key: String?): String? =
        if (key == null) null else requestHeaders.entries.firstOrNull { it.key.equals(key, true) }?.value

    override fun getRequestProperties(): Map<String, List<String>> =
        requestHeaders.mapValues { listOf(it.value) }

    override fun setRequestProperty(key: String, value: String) {
        checkNotConnected()
        requestHeaders[key] = value
    }

    override fun addRequestProperty(key: String, value: String) {
        checkNotConnected()
        requestHeaders[key] = listOfNotNull(requestHeaders[key], value).joinToString(", ")
    }

    override fun setRequestMethod(method: String) {
        checkNotConnected()
        super.setRequestMethod(method)
    }

    override fun setFixedLengthStreamingMode(contentLength: Long) {
        require(contentLength >= 0L)
        fixedLength = contentLength
    }

    override fun setFixedLengthStreamingMode(contentLength: Int) = setFixedLengthStreamingMode(contentLength.toLong())

    override fun setChunkedStreamingMode(chunklen: Int) {
        fixedLength = null
    }

    private fun startCall() {
        if (connected) return
        connected = true
        if (doOutput || method in METHODS_WITH_BODY) {
            if (outputPipe == null) {
                val input = PipedInputStream(PIPE_BUFFER_BYTES)
                inputPipe = input
                outputPipe = PipedOutputStream(input).also { it.close() }
            }
        }
        val contentType = requestHeaders.entries.firstOrNull { it.key.equals("Content-Type", true) }
            ?.value?.toMediaTypeOrNull()
        val body = if (inputPipe != null) PipeRequestBody(inputPipe!!, fixedLength, contentType) else null
        val requestBody = if (method in METHODS_WITH_BODY && body == null) {
            ByteArray(0).toRequestBody(contentType)
        } else {
            body
        }
        val requestBuilder = Request.Builder().url(url.toString())
        requestHeaders.forEach { (key, value) -> requestBuilder.header(key, value) }
        val request = requestBuilder.method(method, requestBody).build()
        val client = baseClient.newBuilder()
            .connectTimeout(connectTimeout.toLong(), TimeUnit.MILLISECONDS)
            .readTimeout(readTimeout.toLong(), TimeUnit.MILLISECONDS)
            .callTimeout(0, TimeUnit.MILLISECONDS)
            .followRedirects(instanceFollowRedirects)
            .build()
        call = client.newCall(request).also { activeCall ->
            activeCall.enqueue(object : Callback {
                override fun onFailure(call: Call, e: IOException) {
                    failure = e
                    responseReady.countDown()
                }

                override fun onResponse(call: Call, response: Response) {
                    this@OkHttpHttpURLConnection.response = response
                    responseReady.countDown()
                }
            })
        }
    }

    private fun awaitResponse(): Response {
        startCall()
        try {
            responseReady.await()
        } catch (error: InterruptedException) {
            Thread.currentThread().interrupt()
            throw InterruptedIOException("HTTP request interrupted").also { it.initCause(error) }
        }
        failure?.let { throw it }
        return response ?: throw IOException("HTTP request completed without a response")
    }

    private fun indexedHeader(index: Int): Pair<String?, String?>? {
        if (index < 0) return null
        val result = awaitResponse()
        if (index == 0) return null to "${result.protocol} ${result.code} ${result.message}".trimEnd()
        val headerIndex = index - 1
        if (headerIndex >= result.headers.size) return null
        return result.headers.name(headerIndex) to result.headers.value(headerIndex)
    }

    private fun checkNotConnected() {
        check(!connected) { "Connection is already established" }
    }

    private class PipeRequestBody(
        private val input: InputStream,
        private val length: Long?,
        private val mediaType: MediaType?
    ) : RequestBody() {
        override fun contentType(): MediaType? = mediaType
        override fun contentLength(): Long = length ?: -1L
        override fun writeTo(sink: BufferedSink) {
            input.use { it.copyTo(sink.outputStream()) }
        }
    }

    private companion object {
        const val PIPE_BUFFER_BYTES = 64 * 1024
        val METHODS_WITH_BODY = setOf("POST", "PUT", "PATCH")
    }
}
