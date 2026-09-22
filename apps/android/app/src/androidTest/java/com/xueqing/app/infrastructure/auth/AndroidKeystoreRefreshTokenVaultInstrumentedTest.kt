package com.xueqing.app.infrastructure.auth

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import java.nio.charset.StandardCharsets
import java.security.KeyStore
import kotlin.coroutines.startCoroutine
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AndroidKeystoreRefreshTokenVaultInstrumentedTest {
    private lateinit var context: Context
    private lateinit var vault: AndroidKeystoreRefreshTokenVault

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        vault = AndroidKeystoreRefreshTokenVault(
            context,
            "ci-prod-sg",
            "ci-trust-domain",
        )
        runSuspend { vault.clear() }
    }

    @After
    fun tearDown() {
        runSuspend { vault.clear() }
    }

    @Test
    fun refreshTokenRoundTripsEncryptedOutsideBackupDomains() {
        val token = "fictional-refresh-token-sentinel"
        runSuspend { vault.store(token) }

        assertEquals(token, runSuspend { vault.load() })
        assertTrue(vault.tokenFile.exists())
        assertTrue(
            vault.tokenFile.canonicalPath.startsWith(
                context.noBackupFilesDir.canonicalPath + "/",
            ),
        )

        val persisted = vault.tokenFile.readBytes()
        assertFalse(
            persisted.containsSubsequence(
                token.toByteArray(StandardCharsets.UTF_8),
            ),
        )

        val key = loadKeyStore().getKey(vault.keyAlias, null)
        assertTrue("Refresh-token wrapping key must be non-exportable.", key.encoded == null)
    }

    @Test
    fun tamperedEnvelopeFailsClosed() {
        runSuspend { vault.store("fictional-refresh-token") }
        val bytes = vault.tokenFile.readBytes()
        bytes[bytes.lastIndex] = (bytes.last().toInt() xor 0x01).toByte()
        vault.tokenFile.writeBytes(bytes)

        assertThrows(RefreshTokenVaultUnavailableException::class.java) {
            runSuspend { vault.load() }
        }
    }

    @Test
    fun missingKeystoreKeyFailsClosedWithoutReplacingEnvelope() {
        runSuspend { vault.store("fictional-refresh-token") }
        val before = vault.tokenFile.readBytes()
        loadKeyStore().deleteEntry(vault.keyAlias)

        assertThrows(RefreshTokenVaultUnavailableException::class.java) {
            runSuspend { vault.load() }
        }
        assertTrue(before.contentEquals(vault.tokenFile.readBytes()))
    }

    @Test
    fun clearRemovesEnvelopeAndWrappingKey() {
        runSuspend { vault.store("fictional-refresh-token") }

        runSuspend { vault.clear() }

        assertFalse(vault.tokenFile.exists())
        assertFalse(loadKeyStore().containsAlias(vault.keyAlias))
        assertNull(runSuspend { vault.load() })
    }

    private fun loadKeyStore(): KeyStore =
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

    private fun <T> runSuspend(block: suspend () -> T): T {
        var result: Result<T>? = null
        block.startCoroutine(
            object : kotlin.coroutines.Continuation<T> {
                override val context = kotlin.coroutines.EmptyCoroutineContext

                override fun resumeWith(value: Result<T>) {
                    result = value
                }
            },
        )
        return checkNotNull(result).getOrThrow()
    }
}
