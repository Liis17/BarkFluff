package com.barkfluff.client.auth

import android.content.Intent
import android.net.Uri
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import com.barkfluff.client.R
import com.barkfluff.client.domain.auth.AuthenticationChallengeController
import com.barkfluff.client.domain.gateway.AuthenticationChallengeGateway
import com.barkfluff.client.domain.model.AuthenticationChallenge
import com.barkfluff.client.domain.model.AuthenticationChallengeState
import com.barkfluff.client.domain.model.AuthenticationCompletion
import com.google.android.material.button.MaterialButton
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import kotlin.coroutines.resume
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine

/** Shared Material confirmation UI for the short-lived Identity challenge protocol. */
class AuthenticationChallengeDialog(
    private val activity: AppCompatActivity,
    private val gateway: AuthenticationChallengeGateway,
    private val controller: AuthenticationChallengeController = AuthenticationChallengeController(gateway),
) {
    suspend fun run(
        title: String,
        useRecoveryCode: Boolean = false,
        begin: suspend () -> Result<AuthenticationChallenge>,
    ): Result<AuthenticationCompletion> = suspendCancellableCoroutine { continuation ->
        val padding = (24 * activity.resources.displayMetrics.density).toInt()
        val content = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(padding, 0, padding, 0)
        }
        val status = TextView(activity).apply {
            setTextAppearance(android.R.style.TextAppearance_Material)
        }
        val error = TextView(activity).apply {
            setTextAppearance(android.R.style.TextAppearance_Material_Small)
            setTextColor(activity.getColor(android.R.color.holo_red_dark))
            visibility = View.GONE
        }
        val codeLayout = TextInputLayout(activity).apply {
            hint = activity.getString(if (useRecoveryCode) R.string.auth_recovery_code else R.string.auth_confirmation_code)
            visibility = View.GONE
        }
        val code = TextInputEditText(activity).apply {
            setSingleLine()
            if (!useRecoveryCode) setAutofillHints("oneTimeCode")
        }
        codeLayout.addView(code)
        val telegram = MaterialButton(activity).apply {
            text = activity.getString(R.string.auth_open_telegram)
            visibility = View.GONE
        }
        val resend = MaterialButton(activity).apply {
            text = activity.getString(R.string.auth_resend)
            visibility = View.GONE
        }
        content.addView(status)
        content.addView(error)
        content.addView(codeLayout)
        content.addView(telegram)
        content.addView(resend)

        var completed = false
        var dialog = MaterialAlertDialogBuilder(activity)
            .setTitle(title)
            .setView(content)
            .setNegativeButton(R.string.btn_cancel, null)
            .setPositiveButton(R.string.btn_confirm, null)
            .create()

        fun finish(result: Result<AuthenticationCompletion>) {
            if (completed) return
            completed = true
            dialog.dismiss()
            if (continuation.isActive) continuation.resume(result)
        }

        suspend fun showCompletion() {
            val result = controller.complete(code.text?.toString()?.trim().orEmpty(), useRecoveryCode)
            result.onFailure { error.text = it.message ?: activity.getString(R.string.auth_error); error.visibility = View.VISIBLE }
            result.onSuccess { completion ->
                when {
                    completion.errorCode.isNotBlank() -> {
                        error.text = activity.getString(R.string.auth_invalid_code)
                        error.visibility = View.VISIBLE
                    }
                    completion.state == AuthenticationChallengeState.COMPLETED -> finish(Result.success(completion))
                    else -> {
                        error.text = activity.getString(R.string.auth_challenge_expired)
                        error.visibility = View.VISIBLE
                    }
                }
            }
        }

        fun showTelegram(url: String) {
            telegram.visibility = if (url.isBlank()) View.GONE else View.VISIBLE
            telegram.setOnClickListener {
                runCatching { activity.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) }
                    .onFailure { error.text = activity.getString(R.string.auth_telegram_unavailable); error.visibility = View.VISIBLE }
            }
        }

        lateinit var render: suspend (AuthenticationChallenge) -> Boolean
        render = { challenge ->
            if (challenge.errorCode.isNotBlank()) {
                error.text = if (challenge.errorCode == "login_mode_disabled") {
                    activity.getString(R.string.auth_login_mode_disabled)
                } else {
                    activity.getString(R.string.auth_error)
                }
                error.visibility = View.VISIBLE
                false
            } else {
                status.text = when (challenge.state) {
                    AuthenticationChallengeState.REJECTED -> activity.getString(R.string.auth_challenge_rejected)
                    AuthenticationChallengeState.EXPIRED -> activity.getString(R.string.auth_challenge_expired)
                    AuthenticationChallengeState.CANCELLED -> activity.getString(R.string.auth_challenge_cancelled)
                    AuthenticationChallengeState.APPROVED -> activity.getString(R.string.auth_challenge_approved)
                    else -> if (challenge.needsCode) activity.getString(R.string.auth_enter_code) else activity.getString(R.string.auth_waiting)
                }
                codeLayout.visibility = if (challenge.needsCode) View.VISIBLE else View.GONE
                dialog.getButton(androidx.appcompat.app.AlertDialog.BUTTON_POSITIVE)?.visibility =
                    if (challenge.needsCode) View.VISIBLE else View.GONE
                showTelegram(challenge.telegramUrl)
                resend.visibility = if (
                    !useRecoveryCode &&
                    challenge.factor != com.barkfluff.client.domain.model.AuthenticationFactor.AUTHENTICATOR &&
                    challenge.state == AuthenticationChallengeState.WAITING
                ) View.VISIBLE else View.GONE
                when (challenge.state) {
                    AuthenticationChallengeState.APPROVED -> {
                        showCompletion()
                        false
                    }
                    AuthenticationChallengeState.REJECTED,
                    AuthenticationChallengeState.EXPIRED,
                    AuthenticationChallengeState.CANCELLED -> false
                    else -> true
                }
            }
        }

        dialog.setOnShowListener {
            dialog.getButton(androidx.appcompat.app.AlertDialog.BUTTON_POSITIVE).setOnClickListener {
                activity.lifecycleScope.launch { showCompletion() }
            }
            resend.isEnabled = false
            activity.lifecycleScope.launch {
                delay(60_000)
                if (!completed) resend.isEnabled = true
            }
            resend.setOnClickListener {
                it.isEnabled = false
                activity.lifecycleScope.launch {
                    controller.resend().onSuccess { response -> render(response) }
                        .onFailure { failure -> error.text = failure.message ?: activity.getString(R.string.auth_error); error.visibility = View.VISIBLE }
                    delay(60_000)
                    if (!completed) it.isEnabled = true
                }
            }
            activity.lifecycleScope.launch {
                activity.repeatOnLifecycle(Lifecycle.State.RESUMED) {
                    if (!completed) {
                        controller.resumeOrBegin { begin() }
                            .onSuccess { response ->
                                render(response)
                                controller.poll { update -> render(update) }
                            }
                            .onFailure { failure ->
                                error.text = failure.message ?: activity.getString(R.string.auth_error)
                                error.visibility = View.VISIBLE
                            }
                    }
                }
            }
        }
        dialog.setOnCancelListener {
            if (!completed) {
                activity.lifecycleScope.launch { controller.cancel() }
                finish(Result.failure(java.util.concurrent.CancellationException("Authentication challenge cancelled")))
            }
        }
        continuation.invokeOnCancellation {
            if (!activity.isChangingConfigurations) {
                activity.lifecycleScope.launch { controller.cancel() }
            }
        }
        dialog.show()
    }
}
