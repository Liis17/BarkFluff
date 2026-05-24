package com.barkfluff.clientv2.di

import android.content.Context
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.repository.ChatRepository

/**
 * Ручной DI-контейнер (как BarkFluffApplication в V1): держит singletons не-UI слоя из :core.
 * Создаётся один раз в [com.barkfluff.clientv2.BarkFluffV2Application] и прокидывается в дерево
 * Compose через [LocalAppContainer]. ViewModel получают его через фабрику.
 */
class AppContainer(context: Context) {
    val appContext: Context = context.applicationContext

    val globalParam: GlobalParam = GlobalParam(appContext)
    val grpcManager: GrpcManager = GrpcManager()
    // sideEffects = null: уведомления/виджеты — последующий этап (см. RealtimeSideEffects).
    val realtimeService: RealtimeService = RealtimeService(appContext, grpcManager)
    val chatRepository: ChatRepository = ChatRepository(appContext, grpcManager)
}
