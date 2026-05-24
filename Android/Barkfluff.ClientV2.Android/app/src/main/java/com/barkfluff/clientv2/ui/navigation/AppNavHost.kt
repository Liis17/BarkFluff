package com.barkfluff.clientv2.ui.navigation

import androidx.compose.runtime.Composable
import androidx.navigation.NavHostController
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.barkfluff.clientv2.ui.screens.chat.ChatScreen
import com.barkfluff.clientv2.ui.screens.home.HomeScreen
import com.barkfluff.clientv2.ui.screens.login.LoginScreen
import com.barkfluff.clientv2.ui.screens.profile.EditProfileScreen
import com.barkfluff.clientv2.ui.screens.search.SearchScreen
import com.barkfluff.clientv2.ui.screens.settings.PrivacyScreen
import com.barkfluff.clientv2.ui.screens.settings.SettingsScreen
import com.barkfluff.clientv2.ui.screens.server.SelectServerScreen
import com.barkfluff.clientv2.ui.screens.splash.SplashScreen
import com.barkfluff.clientv2.ui.screens.welcome.WelcomeScreen

/** Навигационный граф MVP: Splash → Welcome → SelectServer → Login → Home(Chats/Profile) → Chat. */
@Composable
fun AppNavHost(navController: NavHostController = rememberNavController()) {
    NavHost(navController = navController, startDestination = Routes.SPLASH) {
        composable(Routes.SPLASH) {
            SplashScreen(onResolved = { destination ->
                navController.navigate(destination) {
                    popUpTo(Routes.SPLASH) { inclusive = true }
                }
            })
        }
        composable(Routes.WELCOME) {
            WelcomeScreen(onStart = { navController.navigate(Routes.SELECT_SERVER) })
        }
        composable(Routes.SELECT_SERVER) {
            SelectServerScreen(onConnected = { navController.navigate(Routes.LOGIN) })
        }
        composable(Routes.LOGIN) {
            LoginScreen(onLoggedIn = {
                navController.navigate(Routes.CHATS) {
                    popUpTo(navController.graph.id) { inclusive = true }
                }
            })
        }
        composable(Routes.CHATS) {
            HomeScreen(
                onOpenChat = { chatId -> navController.navigate(Routes.chat(chatId)) },
                onLogout = {
                    navController.navigate(Routes.LOGIN) {
                        popUpTo(navController.graph.id) { inclusive = true }
                    }
                },
                onSearch = { navController.navigate(Routes.SEARCH) },
                onEditProfile = { navController.navigate(Routes.PROFILE_EDIT) },
                onOpenSettings = { navController.navigate(Routes.SETTINGS) }
            )
        }
        composable(Routes.PROFILE_EDIT) {
            EditProfileScreen(
                onBack = { navController.popBackStack() },
                onSaved = { navController.popBackStack() }
            )
        }
        composable(Routes.SETTINGS) {
            SettingsScreen(
                onBack = { navController.popBackStack() },
                onOpenPrivacy = { navController.navigate(Routes.PRIVACY) }
            )
        }
        composable(Routes.PRIVACY) {
            PrivacyScreen(onBack = { navController.popBackStack() })
        }
        composable(Routes.SEARCH) {
            SearchScreen(
                onBack = { navController.popBackStack() },
                onOpenChat = { chatId -> navController.navigate(Routes.chat(chatId)) }
            )
        }
        composable(
            route = Routes.CHAT,
            arguments = listOf(navArgument(Routes.CHAT_ARG_ID) { type = NavType.StringType })
        ) { entry ->
            val chatId = entry.arguments?.getString(Routes.CHAT_ARG_ID).orEmpty()
            ChatScreen(chatId = chatId, onBack = { navController.popBackStack() })
        }
    }
}
