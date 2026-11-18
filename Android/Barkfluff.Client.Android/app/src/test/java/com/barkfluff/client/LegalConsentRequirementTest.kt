package com.barkfluff.client

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class LegalConsentRequirementTest {
    @Test
    fun `consent is required until the current revision is accepted`() {
        assertTrue(WelcomeActivity.legalConsentRequired(currentRevision = "2026-11", acceptedRevision = ""))
        assertTrue(WelcomeActivity.legalConsentRequired(currentRevision = "2026-11", acceptedRevision = "2026-01"))
        assertFalse(WelcomeActivity.legalConsentRequired(currentRevision = "2026-11", acceptedRevision = "2026-11"))
    }

    @Test
    fun `missing legal documents do not block node selection`() {
        assertFalse(WelcomeActivity.legalConsentRequired(currentRevision = "", acceptedRevision = ""))
    }
}
