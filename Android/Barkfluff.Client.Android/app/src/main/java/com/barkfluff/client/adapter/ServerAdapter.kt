package com.barkfluff.client.adapter

import android.content.res.ColorStateList
import android.os.SystemClock
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import com.barkfluff.client.data.ServerDataElement
import com.google.android.material.card.MaterialCardView
import com.google.android.material.chip.Chip
import com.google.android.material.color.MaterialColors
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

private const val RESPONSE_REFRESH_INTERVAL_MS = 2_000L

/**
 * Адаптер для списка серверов
 */
class ServerAdapter(
    private val coroutineScope: CoroutineScope,
    private val measureResponseMs: suspend (String) -> Int?,
    private val onServerClick: (ServerDataElement) -> Unit
) : ListAdapter<ServerDataElement, ServerAdapter.ServerViewHolder>(ServerDiffCallback()) {

    private val attachedHolders = linkedSetOf<ServerViewHolder>()
    private var probingEnabled = true

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ServerViewHolder {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_server, parent, false)
        return ServerViewHolder(view, onServerClick)
    }

    override fun onBindViewHolder(holder: ServerViewHolder, position: Int) {
        holder.bind(getItem(position), coroutineScope, measureResponseMs)
        if (probingEnabled && holder.itemView.isAttachedToWindow) {
            holder.startProbe()
        }
    }

    override fun onViewAttachedToWindow(holder: ServerViewHolder) {
        super.onViewAttachedToWindow(holder)
        attachedHolders += holder
        if (probingEnabled) {
            holder.startProbe()
        }
    }

    override fun onViewDetachedFromWindow(holder: ServerViewHolder) {
        holder.cancelPendingProbe()
        attachedHolders -= holder
        super.onViewDetachedFromWindow(holder)
    }

    override fun onViewRecycled(holder: ServerViewHolder) {
        super.onViewRecycled(holder)
        attachedHolders -= holder
        holder.clearProbeBinding()
    }

    fun setProbingEnabled(enabled: Boolean) {
        probingEnabled = enabled
        attachedHolders.toList().forEach { holder ->
            if (enabled) {
                holder.startProbe()
            } else {
                holder.cancelPendingProbe()
            }
        }
    }

    class ServerViewHolder(
        itemView: View,
        private val onServerClick: (ServerDataElement) -> Unit
    ) : RecyclerView.ViewHolder(itemView) {

        private val serverIconTile: MaterialCardView = itemView.findViewById(R.id.serverIconTile)
        private val title: TextView = itemView.findViewById(R.id.serverTitle)
        private val description: TextView = itemView.findViewById(R.id.serverDescription)
        private val handle: TextView = itemView.findViewById(R.id.serverHandle)
        private val chipOnline: Chip = itemView.findViewById(R.id.chipOnline)
        private val chipResponse: Chip = itemView.findViewById(R.id.chipResponse)
        private val chipRegion: Chip = itemView.findViewById(R.id.chipRegion)
        private val connectCta = itemView.findViewById<com.google.android.material.button.MaterialButton>(R.id.serverConnectCta)

        private var probeJob: Job? = null
        private var boundAddress: String? = null
        private var boundCoroutineScope: CoroutineScope? = null
        private var probeMeasureResponseMs: (suspend (String) -> Int?)? = null

        private enum class ServerStatus {
            CHECKING,
            ONLINE,
            UNAVAILABLE,
        }

        fun cancelPendingProbe() {
            probeJob?.cancel()
            probeJob = null
        }

        fun clearProbeBinding() {
            cancelPendingProbe()
            boundAddress = null
            boundCoroutineScope = null
            probeMeasureResponseMs = null
            itemView.tag = null
        }

        fun bind(server: ServerDataElement, coroutineScope: CoroutineScope, measureResponseMs: suspend (String) -> Int?) {
            cancelPendingProbe()
            itemView.tag = server.ip
            boundAddress = server.ip
            boundCoroutineScope = coroutineScope
            probeMeasureResponseMs = measureResponseMs
            title.text = server.title
            description.text = server.description

            // Чипы информируют о состоянии, но не являются отдельными действиями.
            listOf(chipOnline, chipResponse, chipRegion).forEach { chip ->
                chip.isClickable = false
                chip.isFocusable = false
                chip.isFocusableInTouchMode = false
                chip.importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_YES
            }

            // Макет 2c: регион — чип в общей строке, публичное имя — отдельная строка ниже
            if (server.location.isNotBlank()) {
                chipRegion.text = server.location
                chipRegion.visibility = View.VISIBLE
            } else {
                chipRegion.visibility = View.GONE
            }

            if (server.publicName.isNotBlank()) {
                handle.text = itemView.context.getString(R.string.server_item_handle, server.publicName)
                handle.visibility = View.VISIBLE
            } else {
                handle.visibility = View.GONE
            }

            chipOnline.visibility = View.VISIBLE
            chipResponse.visibility = View.GONE
            setStatus(ServerStatus.CHECKING)

            // Единственное действие карточки — явная кнопка подключения.
            connectCta.setOnClickListener {
                onServerClick(server)
            }

        }

        fun startProbe() {
            if (probeJob?.isActive == true) return

            val address = boundAddress ?: return
            val scope = boundCoroutineScope ?: return
            val measureResponseMs = probeMeasureResponseMs ?: return

            setStatus(ServerStatus.CHECKING)
            chipResponse.text = ""
            chipResponse.visibility = View.GONE

            // Пробуем сразу, затем обновляем отклик после каждой завершённой проверки.
            // Интервал считается от старта проверки и не допускает наложения запросов
            // даже при медленном Beacon.
            probeJob = scope.launch {
                while (isActive && itemView.tag == address) {
                    val probeStartedAt = SystemClock.elapsedRealtime()
                    val ms = measureResponseMs(address)
                    if (!isActive || itemView.tag != address) break

                    if (ms != null) {
                        setStatus(ServerStatus.ONLINE)
                        chipResponse.text = itemView.context.getString(R.string.server_response_ms, ms)
                        chipResponse.visibility = View.VISIBLE
                    } else {
                        setStatus(ServerStatus.UNAVAILABLE)
                        chipResponse.visibility = View.GONE
                    }

                    val elapsed = SystemClock.elapsedRealtime() - probeStartedAt
                    delay((RESPONSE_REFRESH_INTERVAL_MS - elapsed).coerceAtLeast(0L))
                }
            }
        }

        private fun setStatus(status: ServerStatus) {
            val presentation = when (status) {
                ServerStatus.CHECKING -> StatusPresentation(
                    textRes = R.string.server_status_checking,
                    backgroundColor = MaterialColors.getColor(
                        itemView,
                        com.google.android.material.R.attr.colorSurfaceContainerHighest,
                    ),
                    contentColor = MaterialColors.getColor(
                        itemView,
                        com.google.android.material.R.attr.colorOnSurfaceVariant,
                    ),
                )

                ServerStatus.ONLINE -> StatusPresentation(
                    textRes = R.string.server_status_online,
                    backgroundColor = itemView.context.getColor(R.color.onboarding_success_background),
                    contentColor = itemView.context.getColor(R.color.onboarding_success_text),
                )

                ServerStatus.UNAVAILABLE -> StatusPresentation(
                    textRes = R.string.server_status_unavailable,
                    backgroundColor = MaterialColors.getColor(
                        itemView,
                        com.google.android.material.R.attr.colorErrorContainer,
                    ),
                    contentColor = MaterialColors.getColor(
                        itemView,
                        com.google.android.material.R.attr.colorOnErrorContainer,
                    ),
                )
            }

            chipOnline.setText(presentation.textRes)
            chipOnline.setChipBackgroundColor(ColorStateList.valueOf(presentation.backgroundColor))
            chipOnline.setTextColor(presentation.contentColor)
            chipOnline.chipIconTint = ColorStateList.valueOf(presentation.contentColor)
        }

        private data class StatusPresentation(
            val textRes: Int,
            val backgroundColor: Int,
            val contentColor: Int,
        )
    }

    private class ServerDiffCallback : DiffUtil.ItemCallback<ServerDataElement>() {
        override fun areItemsTheSame(oldItem: ServerDataElement, newItem: ServerDataElement): Boolean {
            return oldItem.ip == newItem.ip
        }

        override fun areContentsTheSame(oldItem: ServerDataElement, newItem: ServerDataElement): Boolean {
            return oldItem == newItem
        }
    }
}
