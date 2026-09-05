package com.barkfluff.client.utils

import android.text.style.URLSpan
import android.text.Spanned
import android.widget.TextView
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class MarkdownRendererTest {

    @Test
    fun bareEmailIsRenderedAsOneMailtoLink() {
        val textView = TextView(InstrumentationRegistry.getInstrumentation().targetContext)

        MarkdownRenderer.applyTo(textView, "support@barkfluff.com")

        val rendered = textView.text as Spanned
        val spans = rendered.getSpans(0, rendered.length, URLSpan::class.java)
        assertEquals(1, spans.size)
        assertEquals("support@barkfluff.com", rendered.subSequence(
            rendered.getSpanStart(spans.single()),
            rendered.getSpanEnd(spans.single())
        ).toString())
        assertEquals("mailto:support@barkfluff.com", spans.single().getURL())
    }
}
