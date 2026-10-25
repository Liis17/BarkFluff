package com.barkfluff.client.chat

import android.widget.FrameLayout
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.adapter.MessageAdapter
import com.barkfluff.client.adapter.MessageRowProjector
import com.barkfluff.client.utils.MessageItemAnimator
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Регрессия на реальном RecyclerView + MessageAdapter: запаздывающая догрузка истории
 * сверху не должна прыгать к новейшим сообщениям, когда пользователь уехал вверх и
 * последнее сообщение — его собственное (исходный сценарий бага). Решение принимает
 * production [ChatScrollDecision]; submit-блок зеркалит observeViewModel ChatActivity
 * (тот же порядок: разделитель непрочитанных, затем низ), а якорь вьюпорта сохраняет
 * нативный DiffUtil/LinearLayoutManager — без принудительного восстановления.
 */
@RunWith(AndroidJUnit4::class)
class ChatHistoryScrollInstrumentedTest {
    private val ownUserId = 7L
    private val peerId = 3L

    @Test
    fun delayedHistoryPrependKeepsViewportAnchorInsteadOfJumpingToNewest() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val app = instrumentation.targetContext.applicationContext as BarkFluffApplication
        val scenario = ActivityScenario.launch(MessageScrollHostActivity::class.java)
        try {
            val harness = arrayOfNulls<ScrollHarness>(1)
            scenario.onActivity { activity ->
                val adapter = MessageAdapter(
                    playback = app.audioPlayback,
                    playbackChatId = "history-scroll-test",
                    currentUserId = ownUserId,
                    isGroupChat = false,
                )
                val recycler = RecyclerView(activity).apply {
                    layoutManager = LinearLayoutManager(activity).apply { stackFromEnd = true }
                    itemAnimator = MessageItemAnimator()
                    this.adapter = adapter
                }
                activity.hostContent.addView(
                    recycler,
                    FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT),
                )
                harness[0] = ScrollHarness(scenario, recycler, adapter, ownUserId)
            }
            val scroll = harness[0]!!

            // Первоначальная страница: последнее сообщение — своё (условие исходного прыжка).
            val initialRows = dayRows("day 1", 1000L..1029L, lastIsOwn = true)
            scroll.submit(initialRows)
            scroll.awaitStable(lastMessageId = 1029L)
            assertTrue(scroll.isAtBottom())

            // Пользователь быстро уехал вверх и ждёт незагруженную историю.
            scroll.scrollToMessage(1005L)
            scroll.awaitStable(lastMessageId = 1029L)
            val anchorBefore = scroll.anchor()
            assertEquals(1005L, anchorBefore.messageId)
            assertFalse(scroll.isAtBottom())

            // Запаздывающая страница истории за предыдущий день: контент + разделитель даты.
            val historyRows = dayRows("day 0", 900L..919L)
            scroll.submit(historyRows + initialRows)
            scroll.awaitStable(lastMessageId = 1029L)

            val anchorAfter = scroll.anchor()
            assertEquals("Видимая строка должна остаться тем же сообщением", anchorBefore.messageId, anchorAfter.messageId)
            assertEquals(
                "Позиция должна сместиться ровно на размер препенда (20 сообщений + разделитель даты)",
                anchorBefore.position + historyRows.size,
                anchorAfter.position,
            )
            assertEquals("Пиксельный сдвиг якоря должен быть сохранён", anchorBefore.top.toFloat(), anchorAfter.top.toFloat(), 2f)
            assertFalse("Вьюпорт не должен оказаться у новых сообщений", scroll.isAtBottom())
        } finally {
            scenario.close()
        }
    }

    private fun message(id: Long, senderId: Long) = MessageItem(
        messageId = id,
        senderId = senderId,
        text = "Сообщение $id\nвторая строка\nтретья строка",
        timestamp = id,
        attachments = emptyList(),
    )

    private fun dayRows(dayLabel: String, ids: LongRange, lastIsOwn: Boolean = false): List<MessageItem> {
        val rows = mutableListOf<MessageItem>(MessageItem.createDateSeparator(dayLabel))
        ids.forEachIndexed { index, id ->
            val sender = if (lastIsOwn && index == ids.count() - 1) ownUserId
                else if (id % 2 == 0L) peerId else ownUserId
            rows += message(id, sender)
        }
        return rows
    }

    private data class Anchor(val position: Int, val messageId: Long, val top: Int)

    /**
     * Тот же production-путь подачи строк, что и observeViewModel ChatActivity:
     * решение [ChatScrollDecision.decide] по закоммиченному снимку до submit,
     * commit-callback с guard версии и порядок применения (непрочитанные, затем низ).
     */
    private class ScrollHarness(
        private val scenario: ActivityScenario<MessageScrollHostActivity>,
        private val recycler: RecyclerView,
        private val adapter: MessageAdapter,
        private val currentUserId: Long,
    ) {
        private val projector = MessageRowProjector()
        private var submittedVersion = 0L

        fun submit(items: List<MessageItem>) {
            val commit = CountDownLatch(1)
            scenario.onActivity {
                val wasAtBottom = isAtBottomRaw()
                val decision = ChatScrollDecision.decide(
                    committed = adapter.currentList,
                    next = items,
                    currentUserId = currentUserId,
                    wasAtBottom = wasAtBottom,
                )
                val version = ++submittedVersion
                adapter.submitList(projector.project(items)) {
                    if (version != submittedVersion) return@submitList
                    if (decision.unreadAppeared) {
                        val idx = adapter.currentList.indexOfFirst { it.type == MessageType.UNREAD_SEPARATOR }
                        if (idx >= 0) {
                            (recycler.layoutManager as LinearLayoutManager).scrollToPositionWithOffset(idx, 0)
                        }
                    } else if (decision.scrollToBottom) {
                        val lastIdx = adapter.itemCount - 1
                        if (lastIdx >= 0) recycler.scrollToPosition(lastIdx)
                    }
                    commit.countDown()
                }
            }
            assertTrue("submitList commit callback не выполнен", commit.await(15, TimeUnit.SECONDS))
        }

        fun scrollToMessage(messageId: Long) {
            scenario.onActivity {
                val index = adapter.currentList.indexOfFirst {
                    it.type == MessageType.MESSAGE && it.messageId == messageId
                }
                assertTrue("Сообщение $messageId должно быть в списке", index >= 0)
                // Нулевой отступ прижимает строку к верхнему краю вьюпорта: при
                // reverseLayout=false LinearLayoutManager мерит положительный offset
                // от start, и ненулевой отступ оставлял бы ранние строки над целью.
                // Clamp невозможен: якорь совпадает с краем, перед целью есть строки.
                (recycler.layoutManager as LinearLayoutManager)
                    .scrollToPositionWithOffset(index, 0)
            }
        }

        fun isAtBottom(): Boolean = onMain { isAtBottomRaw() }

        private fun isAtBottomRaw(): Boolean {
            val layoutManager = recycler.layoutManager as LinearLayoutManager
            val total = layoutManager.itemCount
            return total == 0 || layoutManager.findLastCompletelyVisibleItemPosition() >= total - 1
        }

        /** Якорь вьюпорта: первая видимая строка, её сообщение и пиксельный сдвиг сверху. */
        fun anchor(): Anchor = onMain {
            val layoutManager = recycler.layoutManager as LinearLayoutManager
            // firstVisibleItemPosition + findViewByPosition вместо getChildAt(0):
            // позиция проверяется по adapter position, без неоднозначности
            // анимируемых/удаляемых детей на индексе 0.
            val position = layoutManager.findFirstVisibleItemPosition()
            assertTrue("Должна быть видима хотя бы одна строка", position >= 0)
            val child = layoutManager.findViewByPosition(position)
                ?: throw AssertionError("Видимая строка $position должна иметь attached view")
            Anchor(position = position, messageId = adapter.currentList[position].messageId, top = child.top)
        }

        /** Ждёт коммита списка и применённого layout: нет ожидающих применения
         * обновлений адаптера и запросов layout, idle, без анимаций, стабильный якорь. */
        fun awaitStable(lastMessageId: Long) {
            await("список закоммичен") {
                onMain {
                    adapter.currentList.lastOrNull { it.type == MessageType.MESSAGE }?.messageId == lastMessageId
                }
            }
            await("вьюпорт размечен") { onMain { recycler.height > 0 && (recycler.layoutManager as LinearLayoutManager).childCount > 0 } }
            var lastSample: Anchor? = null
            var stableSince = 0L
            await("стабильный якорь") {
                val settled = onMain {
                    recycler.scrollState == RecyclerView.SCROLL_STATE_IDLE &&
                        !recycler.isLayoutRequested &&
                        !recycler.hasPendingAdapterUpdates() &&
                        (recycler.itemAnimator?.isRunning ?: false).not() &&
                        (recycler.layoutManager as LinearLayoutManager).childCount > 0
                }
                if (!settled) {
                    lastSample = null
                    false
                } else {
                    val sample = anchor()
                    val now = android.os.SystemClock.elapsedRealtime()
                    if (sample != lastSample) {
                        lastSample = sample
                        stableSince = now
                        false
                    } else {
                        now - stableSince >= 250L
                    }
                }
            }
        }

        private fun <T> onMain(block: () -> T): T {
            val result = arrayOfNulls<Any?>(1)
            scenario.onActivity { result[0] = block() }
            @Suppress("UNCHECKED_CAST")
            return result[0] as T
        }

        private fun await(what: String, condition: () -> Boolean) {
            val deadline = android.os.SystemClock.elapsedRealtime() + 15_000L
            while (!condition()) {
                if (android.os.SystemClock.elapsedRealtime() >= deadline) {
                    throw AssertionError("Timed out waiting for $what")
                }
                Thread.sleep(50L)
            }
        }
    }
}
