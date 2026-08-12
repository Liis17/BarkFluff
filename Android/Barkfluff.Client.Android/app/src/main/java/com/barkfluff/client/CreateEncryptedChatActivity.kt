package com.barkfluff.client

import android.os.Bundle
import android.view.View
import android.widget.ArrayAdapter
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import barkfluff.users.UsersApiOuterClass
import com.barkfluff.client.databinding.ActivityCreateEncryptedChatBinding
import kotlinx.coroutines.launch

/**
 * Универсальный экран создания E2E-чата: пользователь выбирает тип (PRIVATE / SECRET),
 * вводит peer userId, passphrase (для приватного) или выбирает peerDeviceId (для секретного).
 *
 * MVP: peerId вводится вручную. В дальнейшем заменить на User picker (UserSearchActivity).
 */
class CreateEncryptedChatActivity : AppCompatActivity() {

    private enum class Type { PRIVATE, SECRET }

    private lateinit var binding: ActivityCreateEncryptedChatBinding
    private var type: Type = Type.PRIVATE
    private var peerDevices: List<UsersApiOuterClass.PeerDeviceInfo> = emptyList()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityCreateEncryptedChatBinding.inflate(layoutInflater)
        setContentView(binding.root)

        binding.toolbar.setNavigationOnClickListener { finish() }
        type = when (intent.getStringExtra(EXTRA_INITIAL_TYPE)) {
            INITIAL_TYPE_SECRET -> Type.SECRET
            else -> Type.PRIVATE
        }
        binding.chatTypeToggle.addOnButtonCheckedListener { _, checkedId, isChecked ->
            if (!isChecked) return@addOnButtonCheckedListener
            type = if (checkedId == binding.typePrivateButton.id) Type.PRIVATE else Type.SECRET
            applyTypeUi()
        }
        binding.chatTypeToggle.check(
            if (type == Type.PRIVATE) binding.typePrivateButton.id else binding.typeSecretButton.id
        )

        binding.peerIdEditText.setOnFocusChangeListener { _, hasFocus ->
            if (!hasFocus && type == Type.SECRET) loadPeerDevices()
        }
        binding.createButton.setOnClickListener { onCreate() }
    }

    private fun applyTypeUi() {
        when (type) {
            Type.PRIVATE -> {
                binding.passphraseLayout.visibility = View.VISIBLE
                binding.initialMessageLayout.visibility = View.GONE
                binding.peerDeviceLabel.visibility = View.GONE
                binding.peerDeviceCard.visibility = View.GONE
                binding.modeIcon.setImageResource(R.drawable.ic_key)
                binding.modeTitle.text = getString(R.string.private_chat_title)
                binding.modeDescription.text = getString(R.string.private_chat_description)
            }
            Type.SECRET -> {
                binding.passphraseLayout.visibility = View.GONE
                binding.initialMessageLayout.visibility = View.VISIBLE
                binding.peerDeviceLabel.visibility = View.VISIBLE
                binding.peerDeviceCard.visibility = View.VISIBLE
                binding.modeIcon.setImageResource(R.drawable.ic_security)
                binding.modeTitle.text = getString(R.string.encrypted_chat_type_secret)
                binding.modeDescription.text = getString(R.string.secret_chat_description)
            }
        }
    }

    private fun loadPeerDevices() {
        val peerId = binding.peerIdEditText.text?.toString()?.toLongOrNull() ?: return
        val app = applicationContext as BarkFluffApplication
        lifecycleScope.launch {
            val result = app.grpcManager.listPeerDevices(peerId)
            result.onSuccess { devices ->
                peerDevices = devices.filter { it.hasBundle }
                val labels = if (peerDevices.isEmpty()) listOf(getString(R.string.encrypted_devices_empty))
                    else peerDevices.map { "${it.displayName} (${it.deviceId.take(8)})" }
                binding.peerDeviceSpinner.adapter = ArrayAdapter(
                    this@CreateEncryptedChatActivity,
                    android.R.layout.simple_spinner_dropdown_item,
                    labels
                )
            }.onFailure {
                Toast.makeText(
                    this@CreateEncryptedChatActivity,
                    getString(R.string.encrypted_devices_load_failed, it.message.orEmpty()),
                    Toast.LENGTH_LONG
                ).show()
            }
        }
    }

    private fun onCreate() {
        val peerId = binding.peerIdEditText.text?.toString()?.toLongOrNull()
        if (peerId == null || peerId <= 0) {
            Toast.makeText(this, R.string.encrypted_peer_id_invalid, Toast.LENGTH_SHORT).show()
            return
        }
        when (type) {
            Type.PRIVATE -> createPrivate(peerId)
            Type.SECRET -> createSecret(peerId)
        }
    }

    private fun createPrivate(peerId: Long) {
        val passphrase = binding.passphraseEditText.text?.toString().orEmpty()
        if (passphrase.length < 6) {
            Toast.makeText(this, R.string.encrypted_passphrase_too_short, Toast.LENGTH_SHORT).show()
            return
        }
        val app = applicationContext as BarkFluffApplication
        binding.progressBar.visibility = View.VISIBLE
        binding.createButton.isEnabled = false
        lifecycleScope.launch {
            val result = app.privateChatRepository.createPrivateChat(peerId, passphrase)
            binding.progressBar.visibility = View.GONE
            binding.createButton.isEnabled = true
            result.onSuccess { creation ->
                val chat = creation.chat
                val text = getString(
                    if (creation.created) R.string.private_chat_created else R.string.private_chat_existing_opened
                )
                Toast.makeText(this@CreateEncryptedChatActivity, text, Toast.LENGTH_LONG).show()
                startActivity(ChatActivity.privateChatIntent(
                    this@CreateEncryptedChatActivity,
                    chatId = chat.id,
                    title = chat.title.ifBlank { getString(R.string.private_chat_title) }
                ))
                finish()
            }.onFailure {
                Toast.makeText(
                    this@CreateEncryptedChatActivity,
                    getString(R.string.private_chat_create_failed, it.message.orEmpty()),
                    Toast.LENGTH_LONG
                ).show()
            }
        }
    }

    private fun createSecret(peerId: Long) {
        val initialMessage = binding.initialMessageEditText.text?.toString()?.trim().orEmpty()
        if (initialMessage.isEmpty()) {
            Toast.makeText(this, R.string.secret_initial_message_required, Toast.LENGTH_SHORT).show()
            return
        }
        val selectedIndex = binding.peerDeviceSpinner.selectedItemPosition
        if (peerDevices.isEmpty() || selectedIndex < 0 || selectedIndex >= peerDevices.size) {
            Toast.makeText(this, R.string.secret_peer_no_devices, Toast.LENGTH_SHORT).show()
            return
        }
        val peerDeviceId = peerDevices[selectedIndex].deviceId
        val app = applicationContext as BarkFluffApplication
        binding.progressBar.visibility = View.VISIBLE
        binding.createButton.isEnabled = false
        lifecycleScope.launch {
            val result = app.secretChatRepository.createSecretChat(peerId, peerDeviceId, initialMessage)
            binding.progressBar.visibility = View.GONE
            binding.createButton.isEnabled = true
            result.onSuccess { chat ->
                Toast.makeText(this@CreateEncryptedChatActivity, R.string.secret_invite_sent, Toast.LENGTH_LONG).show()
                startActivity(ChatActivity.secretChatIntent(
                    this@CreateEncryptedChatActivity,
                    secretChatId = chat.id,
                    initialMessage = initialMessage
                ))
                finish()
            }.onFailure {
                Toast.makeText(
                    this@CreateEncryptedChatActivity,
                    getString(R.string.secret_chat_create_failed, it.message.orEmpty()),
                    Toast.LENGTH_LONG
                ).show()
            }
        }
    }

    companion object {
        const val EXTRA_INITIAL_TYPE = "initial_chat_type"
        const val INITIAL_TYPE_SECRET = "secret"
    }
}
