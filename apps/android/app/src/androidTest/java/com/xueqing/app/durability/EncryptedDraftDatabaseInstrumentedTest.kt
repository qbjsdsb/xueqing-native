package com.xueqing.app.durability

import android.content.Context
import android.util.AtomicFile
import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import java.nio.charset.StandardCharsets
import java.security.KeyStore
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class EncryptedDraftDatabaseInstrumentedTest {
    private lateinit var context: Context

    private val scope = DraftScope(
        environmentId = "ci-encryption",
        appUserId = "user-encryption",
        organizationId = "org-encryption",
        studentId = "student-encryption",
        subjectId = "subject-chinese",
        contextId = "quick-capture",
    )

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
    fun correctWrappedKeyReopensEncryptedDatabase() = runBlocking {
        val first = DraftDatabase.create(context)
        val firstStore = DraftStore(first.draftDao()) { 1_789_632_000_000L }
        val session = firstStore.open(scope)
        assertTrue(firstStore.save(session, "加密重开：课堂观察仍然存在。"))
        first.close()

        val second = DraftDatabase.create(context)
        val secondStore = DraftStore(second.draftDao()) { 1_789_632_000_001L }
        assertTrue(secondStore.load(scope)?.text == "加密重开：课堂观察仍然存在。")
        second.close()
    }

    @Test
    fun plaintextPreProductionDatabaseFailsClosedInsteadOfSilentlyMigrating() = runBlocking {
        val plaintext = Room.databaseBuilder(
            context,
            DraftDatabase::class.java,
            DraftDatabase.DATABASE_NAME,
        ).build()
        val store = DraftStore(plaintext.draftDao()) { 1_789_632_000_000L }
        val session = store.open(scope)
        assertTrue(store.save(session, "PREPRODUCTION_PLAINTEXT_MUST_NOT_AUTO_MIGRATE"))
        plaintext.close()

        val databaseFile = context.getDatabasePath(DraftDatabase.DATABASE_NAME)
        val before = databaseFile.readBytes()
        assertFalse(EncryptedDatabaseKeyManager.wrappedKeyFile(context).exists())

        assertThrows(LocalDataKeyUnavailableException::class.java) {
            DraftDatabase.create(context)
        }
        assertArrayEquals(before, databaseFile.readBytes())
    }

    @Test
    fun missingWrappedKeyEnvelopeFailsClosedWithoutReplacingDatabase() = runBlocking {
        val database = createAndPersist("missing-envelope-sentinel")
        database.close()
        val databaseFile = context.getDatabasePath(DraftDatabase.DATABASE_NAME)
        val before = databaseFile.readBytes()

        AtomicFile(EncryptedDatabaseKeyManager.wrappedKeyFile(context)).delete()
        assertThrows(LocalDataKeyUnavailableException::class.java) {
            DraftDatabase.create(context)
        }
        assertArrayEquals(before, databaseFile.readBytes())
    }

    @Test
    fun missingKeystoreWrappingKeyFailsClosed() = runBlocking {
        val database = createAndPersist("missing-keystore-sentinel")
        database.close()
        val databaseFile = context.getDatabasePath(DraftDatabase.DATABASE_NAME)
        val before = databaseFile.readBytes()

        loadAndroidKeyStore().deleteEntry(EncryptedDatabaseKeyManager.WRAPPING_KEY_ALIAS)
        assertThrows(LocalDataKeyUnavailableException::class.java) {
            DraftDatabase.create(context)
        }
        assertArrayEquals(before, databaseFile.readBytes())
    }

    @Test
    fun databaseWalAndShmDoNotContainPlaintextSentinel() = runBlocking {
        val sentinel = "XUEQING_PLAINTEXT_SENTINEL_6F7409C2E13A"
        val database = createAndPersist(sentinel)
        database.openHelper.writableDatabase.query("PRAGMA wal_checkpoint(FULL)").use { cursor ->
            cursor.moveToFirst()
        }
        database.close()

        val databaseFile = context.getDatabasePath(DraftDatabase.DATABASE_NAME)
        val persistedFiles = EncryptedDatabaseKeyManager.databaseFiles(databaseFile).filter { it.exists() }
        assertTrue("Expected at least the encrypted primary database file.", persistedFiles.isNotEmpty())
        val needle = sentinel.toByteArray(StandardCharsets.UTF_8)
        persistedFiles.forEach { file ->
            assertFalse("Plaintext sentinel leaked into ${file.name}", file.readBytes().containsSubsequence(needle))
        }
    }

    @Test
    fun wrappingKeyIsNonExportableAndEnvelopeLivesOutsideBackupDomains() {
        val database = DraftDatabase.create(context)
        database.openHelper.writableDatabase
        database.close()

        val wrappingKey = loadAndroidKeyStore().getKey(EncryptedDatabaseKeyManager.WRAPPING_KEY_ALIAS, null)
        assertTrue("Android Keystore key must be non-exportable.", wrappingKey.encoded == null)

        val envelope = EncryptedDatabaseKeyManager.wrappedKeyFile(context)
        assertTrue(envelope.exists())
        assertTrue(envelope.canonicalPath.startsWith(context.noBackupFilesDir.canonicalPath + "/"))
    }

    @Test
    fun explicitPurgeRemovesDatabaseAndKeyMaterialAndOldDraftDoesNotReturn() = runBlocking {
        val database = createAndPersist("purge-sentinel")
        database.close()
        val databaseFile = context.getDatabasePath(DraftDatabase.DATABASE_NAME)
        assertTrue(databaseFile.exists())
        assertTrue(EncryptedDatabaseKeyManager.wrappedKeyFile(context).exists())
        assertTrue(loadAndroidKeyStore().containsAlias(EncryptedDatabaseKeyManager.WRAPPING_KEY_ALIAS))

        DraftDatabase.purgeLocalEncryptedData(context)

        assertFalse(EncryptedDatabaseKeyManager.hasPersistedDatabaseFiles(databaseFile))
        assertFalse(EncryptedDatabaseKeyManager.wrappedKeyFile(context).exists())
        assertFalse(loadAndroidKeyStore().containsAlias(EncryptedDatabaseKeyManager.WRAPPING_KEY_ALIAS))

        val replacement = DraftDatabase.create(context)
        val replacementStore = DraftStore(replacement.draftDao()) { 1_789_632_000_002L }
        assertNull(replacementStore.load(scope))
        replacement.close()
    }

    private suspend fun createAndPersist(text: String): DraftDatabase {
        val database = DraftDatabase.create(context)
        val store = DraftStore(database.draftDao()) { 1_789_632_000_000L }
        val session = store.open(scope)
        assertTrue(store.save(session, text))
        return database
    }

    private fun loadAndroidKeyStore(): KeyStore =
        KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

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
}
