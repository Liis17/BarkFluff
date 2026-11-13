package com.barkfluff.client.utils

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.widget.Toast
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.LoginActivity
import com.barkfluff.client.R
import com.barkfluff.client.domain.model.AccountRestriction
import com.barkfluff.client.domain.model.AccountStatus
import com.barkfluff.client.grpc.AccountRestrictionErrorCodes
import com.barkfluff.client.grpc.TokenValidity
import com.barkfluff.client.grpc.grpcErrorCode
import java.text.DateFormat
import java.util.Date

fun Context.accountStatusLabel(status: AccountStatus): String = when (status) {
    AccountStatus.PENDING_DELETION, AccountStatus.DELETED -> getString(R.string.account_deleted_user)
    AccountStatus.ACTIVE -> ""
    AccountStatus.BLOCKED, AccountStatus.UNKNOWN -> getString(R.string.account_blocked_user)
}

fun Context.accountRestrictionTitle(status: AccountStatus): String = when (status) {
    AccountStatus.PENDING_DELETION -> getString(R.string.account_pending_deletion_title)
    AccountStatus.BLOCKED, AccountStatus.DELETED, AccountStatus.UNKNOWN -> getString(R.string.account_blocked_title)
    AccountStatus.ACTIVE -> ""
}

fun Context.accountRestrictionDetails(restriction: AccountRestriction): String = buildList {
    if (restriction.reason.isNotBlank()) add(getString(R.string.account_restriction_reason, restriction.reason))
    when (restriction.status) {
        AccountStatus.PENDING_DELETION -> {
            restriction.deletionScheduledAtMillis?.let { timestamp ->
                val locale = resources.configuration.locales[0]
                val localDate = DateFormat.getDateTimeInstance(DateFormat.LONG, DateFormat.SHORT, locale)
                    .format(Date(timestamp))
                add(getString(R.string.account_deletion_date, localDate))
            }
            add(getString(R.string.account_pending_deletion_explanation))
        }
        else -> add(getString(R.string.account_blocked_explanation))
    }
}.joinToString("\n\n")

fun Context.accountRestrictionRpcMessage(error: Throwable): String? = when (error.grpcErrorCode()?.uppercase()) {
    AccountRestrictionErrorCodes.ACCOUNT_BLOCKED -> getString(R.string.account_session_blocked)
    AccountRestrictionErrorCodes.ACCOUNT_PENDING_DELETION -> getString(R.string.account_session_pending_deletion)
    AccountRestrictionErrorCodes.USER_UNAVAILABLE -> getString(R.string.account_user_unavailable)
    else -> null
}

fun Context.accountRestrictionSessionMessage(validity: TokenValidity): String? = when (validity) {
    TokenValidity.ACCOUNT_BLOCKED -> getString(R.string.account_session_blocked)
    TokenValidity.ACCOUNT_PENDING_DELETION -> getString(R.string.account_session_pending_deletion)
    else -> null
}

suspend fun Activity.exitToLoginIfAccountRestricted(error: Throwable): Boolean {
    val validity = when (error.grpcErrorCode()?.uppercase()) {
        AccountRestrictionErrorCodes.ACCOUNT_BLOCKED -> TokenValidity.ACCOUNT_BLOCKED
        AccountRestrictionErrorCodes.ACCOUNT_PENDING_DELETION -> TokenValidity.ACCOUNT_PENDING_DELETION
        else -> return false
    }
    val app = applicationContext as BarkFluffApplication
    app.realtimeService.shutdown()
    if (!LogoutHelper.exitRejectedSession(app, app.prekeyManager)) {
        Toast.makeText(this, R.string.crypto_cleanup_failed, Toast.LENGTH_LONG).show()
        return true
    }
    startActivity(Intent(this, LoginActivity::class.java).apply {
        flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
        putExtra(LoginActivity.EXTRA_ACCOUNT_RESTRICTION_NOTICE, accountRestrictionSessionMessage(validity))
    })
    finish()
    return true
}
