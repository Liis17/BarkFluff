package com.barkfluff.messenger.presentation.navigation

sealed class Screen(val route: String) {
    object Splash : Screen("splash")
    object Welcome : Screen("welcome")
    object SelectServer : Screen("select_server")
    object Login : Screen("login")
    object Register : Screen("register")
    object VerifyEmail : Screen("verify_email/{email}") {
        fun createRoute(email: String) = "verify_email/$email"
    }
    object ForgotPassword : Screen("forgot_password")
    object ConfirmResetPassword : Screen("confirm_reset_password/{email}") {
        fun createRoute(email: String) = "confirm_reset_password/$email"
    }
    object ChatList : Screen("chat_list")
    object Chat : Screen("chat/{chatId}") {
        fun createRoute(chatId: String) = "chat/$chatId"
    }
    object NewChat : Screen("new_chat")
    object NewGroup : Screen("new_group")
    object ChatMembers : Screen("chat_members/{chatId}") {
        fun createRoute(chatId: String) = "chat_members/$chatId"
    }
    object Profile : Screen("profile/{userId}") {
        fun createRoute(userId: Long) = "profile/$userId"
    }
    object MyProfile : Screen("my_profile")
    object Settings : Screen("settings")
    object EditProfile : Screen("edit_profile")
    object ActiveSessions : Screen("active_sessions")
    object TwoFactorAuth : Screen("two_factor_auth")
    object ChangePassword : Screen("change_password")
    object ConnectedDevices : Screen("connected_devices")
}
