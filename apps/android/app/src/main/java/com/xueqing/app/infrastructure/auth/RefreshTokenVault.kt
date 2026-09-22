package com.xueqing.app.infrastructure.auth

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.AtomicFile
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.File
import java.nio.charset.StandardCharsets
import java.security.KeyStore
import java.security.MessageDigest
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

interface RefreshTokenVault {
    suspend fun load(): String?
    suspend fun store(refreshToken: String)
    suspend fun clear()
}

class RefreshTokenVaultUnavailableException(
    message: String,
    cause: Throwable? = null,
) : IllegalStateException(message, cause)

/**
 * Device-bound refresh-token vault.
 *
 * The refresh token is encrypted with a non-exportable Android Keystore key.
 * The authenticated envelope lives in noBackupFilesDir and is scoped to one
 * deployment environment/trust domain. It is independent from SQLCipher key
 * material and Durable Intent.
 */
class AndroidKeystoreRefreshTokenVault(
    context: Context,
    private val environmentId: String,
    private val trustDomainId: String,
) : RefreshTokenVault {
    private val appContext = context.applicationContext
    private val scopeHash = sha256Hex("$environmentId\n$trustDomainId").take(32)

    internal val keyAlias: String = "xueqing-refresh-token-v1-$scopeHash"
    internal val tokenFile: File =
        File(appContext.noBackupFilesDir, "xueqing-refresh-token-v1-$scopeHash.bin")

    init {
        require(environmentId.isNotBlank() && environmentId == environmentId.trim()) {
            "environmentId must be a non-blank canonical identifier."
        }
        require(trustDomainId.isNotBlank() && trustDomainId == trustDomainId.trim()) {
            "trustDomainId must be a non-blank canonical identifier."
        }
    }

    override suspend fun load(): String? {
        if (!tokenFile.exists()) return null

        val keyStore = loadKeyStore()
        if (!keyStore.containsAlias(keyAlias)) {
            throw RefreshTokenVaultUnavailableException(
                "Refresh-token envelope exists but its Android Keystore key is unavailable.",
            )
        }

        return try {
            val key = keyStore.getKey(keyAlias, null) as? SecretKey
                ?: throw RefreshTokenVaultUnavailableException(
                    "Refresh-token Android Keystore entry is not an AES SecretKey.",
                )
            decryptEnvelope(AtomicFile(tokenFile).readFully(), key)
        } catch (error: RefreshTokenVaultUnavailableException) {
            throw error
        } catch (error: Exception) {
            throw RefreshTokenVaultUnavailableException(
                "Refresh-token envelope could not be authenticated or decrypted.",
                error,
            )
        }
    }

    override suspend fun store(refreshToken: String) {
        validateToken(refreshToken)
        val keyStore = loadKeyStore()

        if (tokenFile.exists() && !keyStore.containsAlias(keyAlias)) {
            throw RefreshTokenVaultUnavailableException(
                "Existing refresh-token envelope cannot be replaced because its Keystore key is unavailable.",
            )
        }

        val key = if (keyStore.containsAlias(keyAlias)) {
            keyStore.getKey(keyAlias, null) as? SecretKey
                ?: throw RefreshTokenVaultUnavailableException(
                    "Refresh-token Android Keystore entry is not an AES SecretKey.",
                )
        } else {
            generateWrappingKey()
        }

        val plaintext = refreshToken.toByteArray(StandardCharsets.UTF_8)
        try {
            val cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(Cipher.ENCRYPT_MODE, key)
            cipher.updateAAD(aad())
            val ciphertext = cipher.doFinal(plaintext)
            val iv = cipher.iv

            val envelope = ByteArrayOutputStream().use { buffer ->
                DataOutputStream(buffer).use { output ->
                    output.write(ENVELOPE_MAGIC)
                    output.writeByte(ENVELOPE_VERSION)
                    output.writeByte(iv.size)
                    output.writeInt(ciphertext.size)
                    output.write(iv)
                    output.write(ciphertext)
                }
                buffer.toByteArray()
            }

            tokenFile.parentFile?.mkdirs()
            val atomic = AtomicFile(tokenFile)
            val output = atomic.startWrite()
            try {
                output.write(envelope)
                atomic.finishWrite(output)
            } catch (error: Exception) {
                atomic.failWrite(output)
                throw error
            } finally {
                envelope.fill(0)
                ciphertext.fill(0)
                iv.fill(0)
            }
        } catch (error: Exception) {
            throw RefreshTokenVaultUnavailableException(
                "Refresh token could not be stored securely.",
                error,
            )
        } finally {
            plaintext.fill(0)
        }
    }

    override suspend fun clear() {
        AtomicFile(tokenFile).delete()
        if (tokenFile.exists()) {
            throw RefreshTokenVaultUnavailableException(
                "Refresh-token envelope still exists after deletion.",
            )
        }

        val keyStore = loadKeyStore()
        if (keyStore.containsAlias(keyAlias)) {
            keyStore.deleteEntry(keyAlias)
        }
    }

    private fun decryptEnvelope(envelope: ByteArray, key: SecretKey): String {
        try {
            val input = DataInputStream(ByteArrayInputStream(envelope))
            val magic = ByteArray(ENVELOPE_MAGIC.size).also(input::readFully)
            if (!magic.contentEquals(ENVELOPE_MAGIC)) {
                throw RefreshTokenVaultUnavailableException(
                    "Refresh-token envelope has an invalid header.",
                )
            }

            val version = input.readUnsignedByte()
            if (version != ENVELOPE_VERSION) {
                throw RefreshTokenVaultUnavailableException(
                    "Unsupported refresh-token envelope version: $version",
                )
            }

            val ivLength = input.readUnsignedByte()
            if (ivLength !in 12..32) {
                throw RefreshTokenVaultUnavailableException(
                    "Refresh-token envelope has an invalid GCM IV length.",
                )
            }

            val ciphertextLength = input.readInt()
            if (ciphertextLength !in 17..8192 || input.available() != ivLength + ciphertextLength) {
                throw RefreshTokenVaultUnavailableException(
                    "Refresh-token envelope length is invalid.",
                )
            }

            val iv = ByteArray(ivLength).also(input::readFully)
            val ciphertext = ByteArray(ciphertextLength).also(input::readFully)
            val cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(
                Cipher.DECRYPT_MODE,
                key,
                GCMParameterSpec(GCM_TAG_BITS, iv),
            )
            cipher.updateAAD(aad())
            val plaintext = cipher.doFinal(ciphertext)
            try {
                val token = plaintext.toString(StandardCharsets.UTF_8)
                validateToken(token)
                return token
            } finally {
                plaintext.fill(0)
                ciphertext.fill(0)
                iv.fill(0)
            }
        } finally {
            envelope.fill(0)
        }
    }

    private fun aad(): ByteArray =
        "$AAD_PREFIX$environmentId\n$trustDomainId".toByteArray(StandardCharsets.UTF_8)

    private fun generateWrappingKey(): SecretKey {
        val generator = KeyGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_AES,
            KEYSTORE_PROVIDER,
        )
        generator.init(
            KeyGenParameterSpec.Builder(
                keyAlias,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
            )
                .setKeySize(256)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setRandomizedEncryptionRequired(true)
                .build(),
        )
        return generator.generateKey()
    }

    private fun loadKeyStore(): KeyStore =
        KeyStore.getInstance(KEYSTORE_PROVIDER).apply { load(null) }

    private companion object {
        const val KEYSTORE_PROVIDER = "AndroidKeyStore"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val GCM_TAG_BITS = 128
        const val ENVELOPE_VERSION = 1
        const val AAD_PREFIX = "xueqing-native/android/refresh-token/v1/"
        val ENVELOPE_MAGIC = "XQRT".toByteArray(StandardCharsets.US_ASCII)

        fun validateToken(token: String) {
            require(token.isNotBlank()) { "Refresh token must not be blank." }
            require(token.none(Char::isWhitespace)) {
                "Refresh token must not contain whitespace."
            }
        }

        fun sha256Hex(value: String): String =
            MessageDigest.getInstance("SHA-256")
                .digest(value.toByteArray(StandardCharsets.UTF_8))
                .joinToString("") { "%02x".format(it) }
    }
}
