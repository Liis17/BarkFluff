package com.barkfluff.messenger.presentation.navigation

import androidx.compose.runtime.Composable
import androidx.navigation.NavHostController
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.navArgument
import com.barkfluff.messenger.presentation.screens.auth.ForgotPasswordScreen
import com.barkfluff.messenger.presentation.screens.auth.LoginScreen
import com.barkfluff.messenger.presentation.screens.auth.RegisterScreen
import com.barkfluff.messenger.presentation.screens.auth.SelectServerScreen
import com.barkfluff.messenger.presentation.screens.auth.VerifyEmailScreen
import com.barkfluff.messenger.presentation.screens.auth.WelcomeScreen
import com.barkfluff.messenger.presentation.screens.chat.ChatListScreen
import com.barkfluff.messenger.presentation.screens.chat.ChatMembersScreen
import com.barkfluff.messenger.presentation.screens.chat.ChatScreen
import com.barkfluff.messenger.presentation.screens.chat.NewChatScreen
import com.barkfluff.messenger.presentation.screens.chat.NewGroupScreen
import com.barkfluff.messenger.presentation.screens.profile.EditProfileScreen
import com.barkfluff.messenger.presentation.screens.profile.MyProfileScreen
import com.barkfluff.messenger.presentation.screens.profile.ProfileScreen
import com.barkfluff.messenger.presentation.screens.settings.ActiveSessionsScreen
import com.barkfluff.messenger.presentation.screens.settings.ChangePasswordScreen
import com.barkfluff.messenger.presentation.screens.settings.ConnectedDevicesScreen
import com.barkfluff.messenger.presentation.screens.settings.SettingsScreen
import com.barkfluff.messenger.presentation.screens.settings.TwoFactorAuthScreen
import com.barkfluff.messenger.presentation.screens.splash.SplashScreen

@Composable
fun NavGraph(
    navController: NavHostController,
    startDestination: String
) {
    NavHost(
        navController = navController,
        startDestination = startDestination
    ) {
        composable(Screen.Splash.route) {
            SplashScreen(navController = navController)
        }

        composable(Screen.Welcome.route) {
            WelcomeScreen(navController = navController)
        }

        composable(Screen.SelectServer.route) {
            SelectServerScreen(navController = navController)
        }

        composable(Screen.Login.route) {
            LoginScreen(navController = navController)
        }

        composable(Screen.Register.route) {
            RegisterScreen(navController = navController)
        }

        composable(
            route = Screen.VerifyEmail.route,
            arguments = listOf(navArgument("email") { type = NavType.StringType })
        ) { backStackEntry ->
            val email = backStackEntry.arguments?.getString("email") ?: ""
            VerifyEmailScreen(navController = navController, email = email)
        }

        composable(Screen.ChatList.route) {
            ChatListScreen(navController = navController)
        }

        composable(
            route = Screen.Chat.route,
            arguments = listOf(navArgument("chatId") { type = NavType.StringType })
        ) { backStackEntry ->
            val chatId = backStackEntry.arguments?.getString("chatId") ?: ""
            ChatScreen(navController = navController, chatId = chatId)
        }

        composable(Screen.NewChat.route) {
            NewChatScreen(navController = navController)
        }

        composable(Screen.NewGroup.route) {
            NewGroupScreen(navController = navController)
        }

        composable(
            route = Screen.Profile.route,
            arguments = listOf(navArgument("userId") { type = NavType.LongType })
        ) { backStackEntry ->
            val userId = backStackEntry.arguments?.getLong("userId") ?: 0L
            ProfileScreen(navController = navController, userId = userId)
        }

        composable(Screen.MyProfile.route) {
            MyProfileScreen(navController = navController)
        }

        composable(Screen.Settings.route) {
            SettingsScreen(navController = navController)
        }

        composable(Screen.EditProfile.route) {
            EditProfileScreen(navController = navController)
        }

        composable(Screen.ForgotPassword.route) {
            ForgotPasswordScreen(navController = navController)
        }

        composable(Screen.ActiveSessions.route) {
            ActiveSessionsScreen(navController = navController)
        }

        composable(Screen.TwoFactorAuth.route) {
            TwoFactorAuthScreen(navController = navController)
        }

        composable(Screen.ChangePassword.route) {
            ChangePasswordScreen(navController = navController)
        }

        composable(Screen.ConnectedDevices.route) {
            ConnectedDevicesScreen(navController = navController)
        }

        composable(
            route = Screen.ChatMembers.route,
            arguments = listOf(navArgument("chatId") { type = NavType.StringType })
        ) { backStackEntry ->
            val chatId = backStackEntry.arguments?.getString("chatId") ?: ""
            ChatMembersScreen(navController = navController, chatId = chatId)
        }
    }
}
