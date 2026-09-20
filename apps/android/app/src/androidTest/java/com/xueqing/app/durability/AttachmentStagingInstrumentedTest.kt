package com.xueqing.app.durability

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import java.io.ByteArrayInputStream
import java.nio.charset.StandardCharsets
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AttachmentStagingInstrumentedTest {
    private lateinit var context: Context

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        DraftDatabase.purgeLocalEncryptedData(context)
    }

    @After
    fun tearDown() {
        DraftDatabase.purgeLocalEncryptedData(context)
    }

    @Test
    fun stagedBytesAndMetadataSurviveEncryptedReopenWithoutPlaintextAtRest() = runBlocking {
        val attachmentId = UUID.fromString("71000000-0000-0000-0000-000000000001")
        val sentinel = "附件加密证据：课堂图片派生内容不能以明文落盘。".toByteArray(StandardCharsets.UTF_8)

        val first = DraftDatabase.create(context)
        val scope = scope()
        val session = DraftStore(first.draftDao()) { NOW }.open(scope)
        val protectedFiles = ProtectedAttachmentFileStore(context)
        val staging = AttachmentStagingStore(
            dao = first.attachmentStagingDao(),
            protectedFiles = protectedFiles,
            clock = { NOW },
        )
        val row = staging.stageDerivative(
            scope = scope,
            draftEpoch = session.epoch,
            assignmentId = ASSIGNMENT_ID,
            attachmentId = attachmentId,
            contentType = "image/jpeg",
            source = ByteArrayInputStream(sentinel),
        )

        val encrypted = protectedFiles.encryptedFile(row.localEncryptedFileName).readBytes()
        assertFalse(encrypted.containsSubsequence(sentinel))
        first.close()

        val reopened = DraftDatabase.create(context)
        val persisted = reopened.attachmentStagingDao().read(attachmentId.toString())
        assertNotNull(persisted)
        assertEquals(AttachmentStagingState.Staged, persisted?.state)
        val decrypted = ProtectedAttachmentFileStore(context)
            .openDecrypted(requireNotNull(persisted).localEncryptedFileName)
            .use { it.readBytes() }
        assertArrayEquals(sentinel, decrypted)
        reopened.close()
    }

    private fun scope() = DraftScope(
        environmentId = "ci-attachment",
        appUserId = "10000000-0000-0000-0000-000000000001",
        organizationId = "20000000-0000-0000-0000-000000000001",
        studentId = "30000000-0000-0000-0000-000000000001",
        subjectId = "40000000-0000-0000-0000-000000000001",
        contextId = "quick-capture",
    )

    private fun ByteArray.containsSubsequence(needle: ByteArray): Boolean {
        if (needle.isEmpty()) return true
        if (needle.size > size) return false
        for (start in 0..size - needle.size) {
            var matches = true
            for (offset in needle.indices) {
                if (this[start + offset] != needle[offset]) {
                    matches = false
                    break
                }
            }
            if (matches) return true
        }
        return false
    }

    private companion object {
        const val NOW = 1_789_632_000_000L
        const val ASSIGNMENT_ID = "50000000-0000-0000-0000-000000000001"
    }
}
