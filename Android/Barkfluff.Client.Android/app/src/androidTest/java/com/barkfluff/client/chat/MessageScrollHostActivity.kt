package com.barkfluff.client.chat

import android.os.Bundle
import android.widget.FrameLayout
import androidx.appcompat.app.AppCompatActivity
import com.barkfluff.client.R

/**
 * Изолированный хост для instrumentation-проверок прокрутки сообщений: реальный
 * RecyclerView с MessageAdapter без ChatViewModel, сети и авторизации. Объявлен
 * в debug-манифесте приложения (app/src/debug): ActivityScenario запускает
 * компоненты тестового APK в чужом процессе (startActivitySync это отвергает),
 * а процесс приложения заодно видит тему и Material3-ресурсы для MessageAdapter.
 */
class MessageScrollHostActivity : AppCompatActivity() {
    lateinit var hostContent: FrameLayout
        private set

    override fun onCreate(savedInstanceState: Bundle?) {
        // Тема ставится кодом до super.onCreate: MessageAdapter раздаёт
        // Material3-виды (MaterialCardView и т.п.).
        setTheme(R.style.Theme_BarkfluffClientAndroid)
        super.onCreate(savedInstanceState)
        hostContent = FrameLayout(this)
        setContentView(hostContent)
    }
}
