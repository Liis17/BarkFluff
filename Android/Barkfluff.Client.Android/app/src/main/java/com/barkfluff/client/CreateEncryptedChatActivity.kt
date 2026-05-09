package com.barkfluff.client

import android.content.Intent
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
        binding.typePrivateButton.isChecked = true
        binding.chatTypeToggle.addOnButtonCheckedListener { _, checkedId, isChecked ->
            if (!isChecked) return@addOnButtonCheckedListener
            type = if (checkedId == binding.typePrivateButton.id) Type.PRIVATE else Type.SECRET
            applyTypeUi()
        }
        applyTypeUi()

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
                binding.modeTitle.text = "Приватный чат"
                binding.modeDescription.text =
                    "Шифрование по passphrase. Чат доступен на всех ваших устройствах."
            }
            Type.SECRET -> {
                binding.passphraseLayout.visibility = View.GONE
                binding.initialMessageLayout.visibility = View.VISIBLE
                binding.peerDeviceLabel.visibility = View.VISIBLE
                binding.peerDeviceCard.visibility = View.VISIBLE
                binding.modeIcon.setImageResource(R.drawable.ic_security)
                binding.modeTitle.text = "Секретный чат"
                binding.modeDescription.text =
                    "End-to-End через Signal Protocol. Привязан к одному устройству собеседника."
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
                val labels = if (peerDevices.isEmpty()) listOf("Нет устройств с prekey-bundle")
                    else peerDevices.map { "${it.displayName} (${it.deviceId.take(8)})" }
                binding.peerDeviceSpinner.adapter = ArrayAdapter(
                    this@CreateEncryptedChatActivity,
                    android.R.layout.simple_spinner_dropdown_item,
                    labels
                )
            }.onFailure {
                Toast.makeText(this@CreateEncryptedChatActivity, "Не удалось загрузить устройства: ${it.message}", Toast.LENGTH_LONG).show()
            }
        }
    }

    private fun onCreate() {
        val peerId = binding.peerIdEditText.text?.toString()?.toLongOrNull()
        if (peerId == null || peerId <= 0) {
            Toast.makeText(this, "Введите корректный ID собеседника", Toast.LENGTH_SHORT).show()
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
            Toast.makeText(this, "Passphrase должен быть не короче 6 символов", Toast.LENGTH_SHORT).show()
            return
        }
        val app = applicationContext as BarkFluffApplication
        binding.progressBar.visibility = View.VISIBLE
        binding.createButton.isEnabled = false
        lifecycleScope.launch {
            val result = app.privateChatRepository.createPrivateChat(peerId, passphrase)
            binding.progressBar.visibility = View.GONE
            binding.createButton.isEnabled = true
            result.onSuccess { chat ->
                Toast.makeText(this@CreateEncryptedChatActivity, "Приватный чат создан, дождитесь подключения собеседника", Toast.LENGTH_LONG).show()
                val intent = Intent(this@CreateEncryptedChatActivity, PrivateChatActivity::class.java)
                    .putExtra(PrivateChatActivity.EXTRA_CHAT_ID, chat.id)
                    .putExtra(PrivateChatActivity.EXTRA_TITLE, chat.title.ifBlank { "Приватный чат" })
                startActivity(intent)
                finish()
            }.onFailure {
                Toast.makeText(this@CreateEncryptedChatActivity, "Не удалось создать чат: ${it.message}", Toast.LENGTH_LONG).show()
            }
        }
    }

    private fun createSecret(peerId: Long) {
        val initialMessage = binding.initialMessageEditText.text?.toString()?.trim().orEmpty()
        if (initialMessage.isEmpty()) {
            Toast.makeText(this, "Введите первое сообщение", Toast.LENGTH_SHORT).show()
            return
        }
        val selectedIndex = binding.peerDeviceSpinner.selectedItemPosition
        if (peerDevices.isEmpty() || selectedIndex < 0 || selectedIndex >= peerDevices.size) {
            Toast.makeText(this, "У собеседника нет устройств с prekey-bundle", Toast.LENGTH_SHORT).show()
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
                Toast.makeText(this@CreateEncryptedChatActivity, "Секретный чат отправлен, ждём подтверждения", Toast.LENGTH_LONG).show()
                val intent = Intent(this@CreateEncryptedChatActivity, SecretChatActivity::class.java)
                    .putExtra(SecretChatActivity.EXTRA_SECRET_CHAT_ID, chat.id)
                    .putExtra(SecretChatActivity.EXTRA_INITIAL_MESSAGE, initialMessage)
                startActivity(intent)
                finish()
            }.onFailure {
                Toast.makeText(this@CreateEncryptedChatActivity, "Не удалось создать секретный чат: ${it.message}", Toast.LENGTH_LONG).show()
            }
        }
    }
}
