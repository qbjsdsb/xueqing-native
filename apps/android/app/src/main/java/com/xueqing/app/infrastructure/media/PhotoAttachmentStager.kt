package com.xueqing.app.infrastructure.media

import android.content.ContentResolver
import android.net.Uri
import com.xueqing.app.durability.AttachmentStagingEntity
import com.xueqing.app.durability.AttachmentStagingStore
import com.xueqing.app.durability.DraftScope
import java.io.ByteArrayInputStream
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class PhotoAttachmentStager(
    private val contentResolver: ContentResolver,
    private val derivativeFactory: ImageDerivativeFactory,
    private val attachmentStagingStore: AttachmentStagingStore,
) {
    suspend fun stage(
        scope: DraftScope,
        draftEpoch: Long,
        assignmentId: String,
        attachmentId: UUID,
        uri: Uri,
    ): AttachmentStagingEntity {
        val derivative = withContext(Dispatchers.IO) {
            derivativeFactory.create(
                ImageDerivativeFactory.ContentResolverSource(
                    contentResolver = contentResolver,
                    uri = uri,
                ),
            )
        }

        return ByteArrayInputStream(derivative.bytes).use { input ->
            attachmentStagingStore.stageDerivative(
                scope = scope,
                draftEpoch = draftEpoch,
                assignmentId = assignmentId,
                attachmentId = attachmentId,
                contentType = derivative.contentType,
                source = input,
            )
        }
    }
}
