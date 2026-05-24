package com.barkfluff.clientv2

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.CompositionLocalProvider
import com.barkfluff.clientv2.di.LocalAppContainer
import com.barkfluff.clientv2.ui.navigation.AppNavHost
import com.barkfluff.clientv2.ui.theme.BarkFluffTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        val container = (application as BarkFluffV2Application).container
        setContent {
            BarkFluffTheme {
                CompositionLocalProvider(LocalAppContainer provides container) {
                    AppNavHost()
                }
            }
        }
    }
}
