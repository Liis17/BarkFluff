package com.barkfluff.clientv2

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.barkfluff.clientv2.di.LocalAppContainer
import com.barkfluff.clientv2.di.ThemeMode
import com.barkfluff.clientv2.ui.navigation.AppNavHost
import com.barkfluff.clientv2.ui.theme.BarkFluffTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        val container = (application as BarkFluffV2Application).container
        setContent {
            val themeMode by container.settingsStore.themeMode.collectAsStateWithLifecycle()
            val dynamicColor by container.settingsStore.dynamicColor.collectAsStateWithLifecycle()
            val darkTheme = when (themeMode) {
                ThemeMode.SYSTEM -> isSystemInDarkTheme()
                ThemeMode.LIGHT -> false
                ThemeMode.DARK -> true
            }
            BarkFluffTheme(darkTheme = darkTheme, dynamicColor = dynamicColor) {
                CompositionLocalProvider(LocalAppContainer provides container) {
                    AppNavHost()
                }
            }
        }
    }
}
