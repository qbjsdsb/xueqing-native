package com.xueqing.app.durability

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.FilterInputStream
import java.io.IOException
import java.io.InputStream
import java.security.KeyStore
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class ProtectedAttachmentFileStore(
    context: Context,
) {
    data class StagedFile(
        val fileName: String,
        val plaintextByteSize: Long,
    )

    private val appContext = context.applicationContext
    private val stagingDirectory = File(appContext.noBackupFilesDir, DIRECTORY_NAME)

    fun stage(
        attachmentId: UUID,
        source: InputStream,
        maxPlaintextBytes: Long = MAX_STAGED_PLAINTEXT_BYTES,
    ): StagedFile {
        require(maxPlaintextBytes in 1..MAX_STAGED_PLAINTEXT_BYTES)
        val fileName = attachmentId.toString() + ".xqas"
        val target = fileForName(fileName)
        require(!target.exists()) { "Attachment id already has protected staged bytes" }

        if (!stagingDirectory.exists() && !stagingDirectory.mkdirs() && !stagingDirectory.isDirectory) {
            throw IOException("Unable to create protected attachment staging directory")
        }

        val temporary = File(stagingDirectory, ".tmp-" + UUID.randomUUID().toString())
        var succeeded = false
        try {
            val cipher = Cipher.getInstance(CIPHER_TRANSFORMATION).apply {
                init(Cipher.ENCRYPT_MODE, obtainOrCreateKey())
            }
            val iv = cipher.iv
            require(iv.isNotEmpty() && iv.size <= 255)

            var plaintextBytes = 0L
            FileOutputStream(temporary).use { output ->
                output.write(MAGIC)
                output.write(iv.size)
                output.write(iv)

                val buffer = ByteArray(BUFFER_SIZE)
                while (true) {
                    val read = source.read(buffer)
                    if (read < 0) break
                    if (read == 0) continue
                    plaintextBytes = Math.addExact(plaintextBytes, read.toLong())
                    if (plaintextBytes > maxPlaintextBytes) {
                        throw AttachmentTooLargeException(maxPlaintextBytes)
                    }
                    cipher.update(buffer, 0, read)?.let(output::write)
                }
                require(plaintextBytes > 0) { "Empty attachment cannot be staged" }
                output.write(cipher.doFinal())
                output.flush()
                output.fd.sync()
            }

            if (!temporary.renameTo(target)) {
                throw IOException("Unable to atomically publish protected staged attachment")
            }
            succeeded = true
            return StagedFile(
                fileName = fileName,
                plaintextByteSize = plaintextBytes,
            )
        } finally {
            if (!succeeded) {
                temporary.delete()
            }
        }
    }

    fun openDecrypted(fileName: String): InputStream {
        val input = FileInputStream(fileForName(fileName))
        try {
            val magic = ByteArray(MAGIC.size)
            input.readFully(magic)
            if (!magic.contentEquals(MAGIC)) {
                throw IOException("Unsupported protected attachment file format")
            }

            val ivLength = input.read()
            if (ivLength !in MIN_GCM_IV_BYTES..MAX_GCM_IV_BYTES) {
                throw IOException("Invalid protected attachment IV")
            }
            val iv = ByteArray(ivLength)
            input.readFully(iv)

            val cipher = Cipher.getInstance(CIPHER_TRANSFORMATION).apply {
                init(
                    Cipher.DECRYPT_MODE,
                    requireExistingKey(),
                    GCMParameterSpec(GCM_TAG_BITS, iv),
                )
            }
            val cipherStream = javax.crypto.CipherInputStream(input, cipher)
            return object : FilterInputStream(cipherStream) {}
        } catch (failure: Throwable) {
            input.close()
            throw failure
        }
    }

    fun delete(fileName: String): Boolean {
        val file = fileForName(fileName)
        return !file.exists() || file.delete()
    }

    fun listEncryptedFileNamesOlderThan(cutoffEpochMillis: Long): Set<String> =
        stagingDirectory.listFiles()
            ?.asSequence()
            ?.filter {
                it.isFile &&
                    ATTACHMENT_FILE_PATTERN.matches(it.name) &&
                    it.lastModified() <= cutoffEpochMillis
            }
            ?.map { it.name }
            ?.toSet()
            .orEmpty()

    fun deleteTemporaryFilesOlderThan(cutoffEpochMillis: Long): Int {
        var deleted = 0
        stagingDirectory.listFiles()
            ?.asSequence()
            ?.filter {
                it.isFile &&
                    TEMPORARY_FILE_PATTERN.matches(it.name) &&
                    it.lastModified() <= cutoffEpochMillis
            }
            ?.forEach { file ->
                if (file.delete()) {
                    deleted += 1
                }
            }
        return deleted
    }

    internal fun encryptedFile(fileName: String): File = fileForName(fileName)

    private fun fileForName(fileName: String): File {
        require(ATTACHMENT_FILE_PATTERN.matches(fileName)) { "Invalid protected attachment file name" }
        return File(stagingDirectory, fileName)
    }

    private fun obtainOrCreateKey(): SecretKey {
        val keyStore = loadKeyStore()
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }

        val generator = KeyGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_AES,
            "AndroidKeyStore",
        )
        generator.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
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

    private fun requireExistingKey(): SecretKey =
        loadKeyStore().getKey(KEY_ALIAS, null) as? SecretKey
            ?: throw LocalAttachmentKeyUnavailableException()

    private fun loadKeyStore(): KeyStore =
        KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

    private fun InputStream.readFully(target: ByteArray) {
        var offset = 0
        while (offset < target.size) {
            val read = read(target, offset, target.size - offset)
            if (read < 0) throw IOException("Unexpected end of protected attachment header")
            offset += read
        }
    }

    companion object {
        const val KEY_ALIAS = "xueqing-attachment-staging-v1"
        const val MAX_STAGED_PLAINTEXT_BYTES = 6_291_456L

        private const val DIRECTORY_NAME = "attachment-staging-v1"
        private const val CIPHER_TRANSFORMATION = "AES/GCM/NoPadding"
        private const val GCM_TAG_BITS = 128
        private const val MIN_GCM_IV_BYTES = 12
        private const val MAX_GCM_IV_BYTES = 32
        private const val BUFFER_SIZE = 32 * 1024
        private val MAGIC = byteArrayOf(0x58, 0x51, 0x41, 0x53, 0x01)
        private val ATTACHMENT_FILE_PATTERN =
            Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\.xqas$")
        private val TEMPORARY_FILE_PATTERN =
            Regex("^\\.tmp-[0-9a-fA-F-]{36}$")

        fun purgeAll(context: Context) {
            File(context.applicationContext.noBackupFilesDir, DIRECTORY_NAME).deleteRecursively()
            val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
            if (keyStore.containsAlias(KEY_ALIAS)) {
                keyStore.deleteEntry(KEY_ALIAS)
            }
        }
    }
}

class AttachmentTooLargeException(
    val maxBytes: Long,
) : IOException("Attachment exceeds protected staging limit")

class LocalAttachmentKeyUnavailableException :
    IOException("Protected attachment key is unavailable")
