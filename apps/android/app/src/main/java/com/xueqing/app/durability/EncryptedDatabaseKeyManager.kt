package com.xueqing.app.durability

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
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class LocalDataKeyUnavailableException(
    message: String,
    cause: Throwable? = null,
) : IllegalStateException(message, cause)

/**
 * Protects the random SQLCipher password with a non-exportable Android Keystore key.
 *
 * The plaintext database password exists only in process memory. The durable envelope
 * lives in noBackupFilesDir and contains only AES-GCM IV + authenticated ciphertext.
 */
object EncryptedDatabaseKeyManager {
    const val WRAPPING_KEY_ALIAS = "xueqing-durable-intent-wrapping-v1"
    const val ENVELOPE_FILE_NAME = "xueqing-durable-intent-key-v1.bin"

    private const val KEYSTORE_PROVIDER = "AndroidKeyStore"
    private const val TRANSFORMATION = "AES/GCM/NoPadding"
    private const val DATABASE_KEY_BYTES = 32
    private const val GCM_TAG_BITS = 128
    private const val ENVELOPE_VERSION = 1
    private val envelopeMagic = "XQK1".toByteArray(StandardCharsets.US_ASCII)
    private val envelopeAad = "xueqing-durable-intent-db-key-v1".toByteArray(StandardCharsets.UTF_8)

    fun obtainDatabaseKey(context: Context, databaseFile: File): ByteArray {
        val appContext = context.applicationContext
        val envelope = wrappedKeyFile(appContext)
        val keyStore = loadKeyStore()
        val databaseExists = hasPersistedDatabaseFiles(databaseFile)

        if (!envelope.exists()) {
            if (databaseExists) {
                throw LocalDataKeyUnavailableException(
                    "Encrypted database exists but its wrapped key envelope is missing. Refusing to regenerate key material.",
                )
            }

            // No durable database exists, so an orphaned alias cannot unlock any
            // user data. Remove it before provisioning a clean baseline.
            if (keyStore.containsAlias(WRAPPING_KEY_ALIAS)) {
                keyStore.deleteEntry(WRAPPING_KEY_ALIAS)
            }

            val wrappingKey = generateWrappingKey()
            val databaseKey = ByteArray(DATABASE_KEY_BYTES).also(SecureRandom()::nextBytes)
            try {
                writeEnvelope(envelope, wrappingKey, databaseKey)
            } catch (error: Exception) {
                databaseKey.fill(0)
                runCatching { loadKeyStore().deleteEntry(WRAPPING_KEY_ALIAS) }
                throw LocalDataKeyUnavailableException("Failed to provision encrypted database key material.", error)
            }
            return databaseKey
        }

        if (!keyStore.containsAlias(WRAPPING_KEY_ALIAS)) {
            throw LocalDataKeyUnavailableException(
                "Wrapped database key exists but the Android Keystore wrapping key is unavailable.",
            )
        }

        return try {
            val wrappingKey = keyStore.getKey(WRAPPING_KEY_ALIAS, null) as? SecretKey
                ?: throw LocalDataKeyUnavailableException("Android Keystore entry is not an AES SecretKey.")
            readEnvelope(envelope, wrappingKey)
        } catch (error: LocalDataKeyUnavailableException) {
            throw error
        } catch (error: Exception) {
            throw LocalDataKeyUnavailableException("Wrapped database key could not be authenticated or decrypted.", error)
        }
    }

    fun wrappedKeyFile(context: Context): File = File(context.noBackupFilesDir, ENVELOPE_FILE_NAME)

    fun hasPersistedDatabaseFiles(databaseFile: File): Boolean =
        databaseFiles(databaseFile).any(File::exists)

    fun databaseFiles(databaseFile: File): List<File> = listOf(
        databaseFile,
        File(databaseFile.path + "-wal"),
        File(databaseFile.path + "-shm"),
        File(databaseFile.path + "-journal"),
    )

    fun deleteKeyMaterial(context: Context) {
        AtomicFile(wrappedKeyFile(context.applicationContext)).delete()
        val keyStore = loadKeyStore()
        if (keyStore.containsAlias(WRAPPING_KEY_ALIAS)) {
            keyStore.deleteEntry(WRAPPING_KEY_ALIAS)
        }
    }

    private fun generateWrappingKey(): SecretKey {
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE_PROVIDER)
        generator.init(
            KeyGenParameterSpec.Builder(
                WRAPPING_KEY_ALIAS,
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

    private fun writeEnvelope(file: File, wrappingKey: SecretKey, databaseKey: ByteArray) {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, wrappingKey)
        cipher.updateAAD(envelopeAad)
        val ciphertext = cipher.doFinal(databaseKey)
        val iv = cipher.iv

        val bytes = ByteArrayOutputStream().use { buffer ->
            DataOutputStream(buffer).use { output ->
                output.write(envelopeMagic)
                output.writeByte(ENVELOPE_VERSION)
                output.writeByte(iv.size)
                output.writeInt(ciphertext.size)
                output.write(iv)
                output.write(ciphertext)
            }
            buffer.toByteArray()
        }

        file.parentFile?.mkdirs()
        val atomicFile = AtomicFile(file)
        val output = atomicFile.startWrite()
        try {
            output.write(bytes)
            atomicFile.finishWrite(output)
        } catch (error: Exception) {
            atomicFile.failWrite(output)
            throw error
        }
    }

    private fun readEnvelope(file: File, wrappingKey: SecretKey): ByteArray {
        val bytes = AtomicFile(file).readFully()
        val input = DataInputStream(ByteArrayInputStream(bytes))
        val magic = ByteArray(envelopeMagic.size).also(input::readFully)
        if (!magic.contentEquals(envelopeMagic)) {
            throw LocalDataKeyUnavailableException("Wrapped database key envelope has an invalid header.")
        }

        val version = input.readUnsignedByte()
        if (version != ENVELOPE_VERSION) {
            throw LocalDataKeyUnavailableException("Unsupported wrapped database key envelope version: $version")
        }

        val ivLength = input.readUnsignedByte()
        if (ivLength !in 12..32) {
            throw LocalDataKeyUnavailableException("Wrapped database key envelope has an invalid GCM IV length.")
        }
        val ciphertextLength = input.readInt()
        if (ciphertextLength !in (DATABASE_KEY_BYTES + 16)..512) {
            throw LocalDataKeyUnavailableException("Wrapped database key envelope has an invalid ciphertext length.")
        }
        if (input.available() != ivLength + ciphertextLength) {
            throw LocalDataKeyUnavailableException("Wrapped database key envelope length is inconsistent.")
        }

        val iv = ByteArray(ivLength).also(input::readFully)
        val ciphertext = ByteArray(ciphertextLength).also(input::readFully)
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, wrappingKey, GCMParameterSpec(GCM_TAG_BITS, iv))
        cipher.updateAAD(envelopeAad)
        val databaseKey = cipher.doFinal(ciphertext)
        if (databaseKey.size != DATABASE_KEY_BYTES) {
            databaseKey.fill(0)
            throw LocalDataKeyUnavailableException("Unwrapped database key has an invalid length.")
        }
        return databaseKey
    }

    private fun loadKeyStore(): KeyStore =
        KeyStore.getInstance(KEYSTORE_PROVIDER).apply { load(null) }
}
