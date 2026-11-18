package com.barkfluff.client

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import javax.xml.parsers.DocumentBuilderFactory

class BftProxyBackupRulesTest {
    @Test
    fun `encrypted proxy preferences are excluded from backup and device transfer`() {
        val resources = File("src/main/res/xml")
        val fullBackup = DocumentBuilderFactory.newInstance().newDocumentBuilder()
            .parse(File(resources, "backup_rules.xml"))
        val dataExtraction = DocumentBuilderFactory.newInstance().newDocumentBuilder()
            .parse(File(resources, "data_extraction_rules.xml"))
        val manifest = File("src/main/AndroidManifest.xml").readText()

        assertEquals("full-backup-content", fullBackup.documentElement.tagName)
        assertHasPreferenceExclusion(fullBackup.documentElement)
        assertHasPreferenceExclusion(dataExtraction.getElementsByTagName("cloud-backup").item(0))
        assertHasPreferenceExclusion(dataExtraction.getElementsByTagName("device-transfer").item(0))
        assertTrue(manifest.contains("android:fullBackupContent=\"@xml/backup_rules\""))
        assertTrue(manifest.contains("android:dataExtractionRules=\"@xml/data_extraction_rules\""))
    }

    private fun assertHasPreferenceExclusion(parent: org.w3c.dom.Node) {
        val children = parent.childNodes
        assertTrue("No encrypted preference exclusion under ${parent.nodeName}", (0 until children.length).any { index ->
            val child = children.item(index)
            child.nodeName == "exclude" &&
                child.attributes?.getNamedItem("domain")?.nodeValue == "sharedpref" &&
                child.attributes?.getNamedItem("path")?.nodeValue == "bft_proxy_secure_prefs.xml"
        })
    }
}
