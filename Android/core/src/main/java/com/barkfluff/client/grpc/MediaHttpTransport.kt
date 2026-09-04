package com.barkfluff.client.grpc

import android.content.Context
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.security.TlsTransportFactory
import com.barkfluff.client.utils.FileMediaUrl
import java.net.HttpURLConnection
import java.net.URL

/**
 * HTTP boundary for media URLs returned by the Files service.
 *
 * URL rewriting and TLS setup used to be reached through [GrpcTransportFacade], which forced upload,
 * download and preview code to depend on the entire RPC facade. Keeping both policies here makes
 * the boundary injectable in repositories and straightforward to fake in tests.
 */
class MediaHttpTransport(
    context: Context,
    private val tlsTransport: TlsTransportFactory = TlsTransportFactory(context.applicationContext),
    private val mediaOrigin: () -> String = { GlobalParam(context.applicationContext).socketFilesMedia },
) {

    /** Rewrites a Files URL to the node's dedicated media origin when one is configured. */
    fun rewrite(url: String): String = FileMediaUrl.rewrite(url, mediaOrigin())

    /** Opens a rewritten URL and applies the host-scoped TLS configuration before use. */
    fun openConnection(url: String): HttpURLConnection {
        val connection = URL(rewrite(url)).openConnection() as HttpURLConnection
        configure(connection)
        return connection
    }

    /** Applies the same TLS policy to an already-created connection. */
    fun configure(connection: HttpURLConnection) {
        tlsTransport.configure(connection)
    }
}
