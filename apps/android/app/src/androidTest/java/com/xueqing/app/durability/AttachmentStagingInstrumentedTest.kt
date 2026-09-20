package com.xueqing.app.durability

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import java.io.ByteArrayInputStream
import java.io.File
import java.nio.charset.StandardCharsets
import java.security.KeyStore
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.assertNotNull
import org.junit.Assert.fail
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

    @Test
    fun missingKeystoreKeyFailsClosedWhenProtectedBytesAlreadyExist() = runBlocking {
        val protectedFiles = ProtectedAttachmentFileStore(context)
        val first = protectedFiles.stage(
            attachmentId = UUID.fromString("71000000-0000-0000-0000-000000000020"),
            source = ByteArrayInputStream("protected-before-key-loss".toByteArray()),
        )

        KeyStore.getInstance("AndroidKeyStore").apply {
            load(null)
            deleteEntry(ProtectedAttachmentFileStore.KEY_ALIAS)
        }

        try {
            ProtectedAttachmentFileStore(context).stage(
                attachmentId = UUID.fromString("71000000-0000-0000-0000-000000000021"),
                source = ByteArrayInputStream("must-not-get-a-new-key".toByteArray()),
            )
            fail("Expected staging to fail closed when encrypted bytes outlive their Keystore key")
        } catch (_: LocalAttachmentKeyUnavailableException) {
            // Expected: never silently generate replacement key material.
        }

        assertTrue(protectedFiles.encryptedFile(first.fileName).exists())
    }

    @Test
    fun stagedAttachmentNeverLeaksAcrossStudentScope() = runBlocking {
        val database = DraftDatabase.create(context)
        val protectedFiles = ProtectedAttachmentFileStore(context)
        val staging = AttachmentStagingStore(
            dao = database.attachmentStagingDao(),
            protectedFiles = protectedFiles,
            clock = { NOW },
        )
        val originalScope = scope()
        val originalSession = DraftStore(database.draftDao()) { NOW }.open(originalScope)
        staging.stageDerivative(
            scope = originalScope,
            draftEpoch = originalSession.epoch,
            assignmentId = ASSIGNMENT_ID,
            attachmentId = UUID.fromString("71000000-0000-0000-0000-000000000022"),
            contentType = "image/jpeg",
            source = ByteArrayInputStream("student-a-only".toByteArray()),
        )

        val otherScope = originalScope.copy(
            studentId = "30000000-0000-0000-0000-000000000099",
        )
        val otherSession = DraftStore(database.draftDao()) { NOW }.open(otherScope)

        assertTrue(staging.readForDraft(otherScope, otherSession.epoch).isEmpty())
        assertEquals(1, staging.readForDraft(originalScope, originalSession.epoch).size)
        database.close()
    }

    @Test
    fun reconciliationDeletesOnlyStaleUnreferencedFilesAndOldTemporaryFiles() = runBlocking {
        val database = DraftDatabase.create(context)
        val scope = scope()
        val session = DraftStore(database.draftDao()) { NOW }.open(scope)
        val protectedFiles = ProtectedAttachmentFileStore(context)
        val staging = AttachmentStagingStore(
            dao = database.attachmentStagingDao(),
            protectedFiles = protectedFiles,
            clock = { NOW },
        )

        val referenced = staging.stageDerivative(
            scope = scope,
            draftEpoch = session.epoch,
            assignmentId = ASSIGNMENT_ID,
            attachmentId = UUID.fromString("71000000-0000-0000-0000-000000000010"),
            contentType = "image/jpeg",
            source = ByteArrayInputStream("referenced".toByteArray()),
        )
        val orphan = protectedFiles.stage(
            attachmentId = UUID.fromString("71000000-0000-0000-0000-000000000011"),
            source = ByteArrayInputStream("orphan".toByteArray()),
        )

        val referencedFile = protectedFiles.encryptedFile(referenced.localEncryptedFileName)
        val orphanFile = protectedFiles.encryptedFile(orphan.fileName)
        val oldTimestamp = NOW - AttachmentStagingStore.DEFAULT_RECONCILIATION_GRACE_MILLIS - 1
        assertTrue(referencedFile.setLastModified(oldTimestamp))
        assertTrue(orphanFile.setLastModified(oldTimestamp))

        val temporary = File(
            requireNotNull(orphanFile.parentFile),
            ".tmp-71000000-0000-0000-0000-000000000012",
        ).apply {
            writeBytes(byteArrayOf(1, 2, 3))
            assertTrue(setLastModified(oldTimestamp))
        }

        staging.reconcileOrphanedFiles()

        assertTrue(referencedFile.exists())
        assertFalse(orphanFile.exists())
        assertFalse(temporary.exists())
        database.close()
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
