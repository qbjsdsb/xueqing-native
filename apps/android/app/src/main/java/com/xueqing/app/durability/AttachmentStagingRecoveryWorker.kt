package com.xueqing.app.durability

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class AttachmentStagingRecoveryWorker(
    appContext: Context,
    workerParameters: WorkerParameters,
) : CoroutineWorker(appContext, workerParameters) {
    override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
        runCatching {
            val database = DraftDatabase.get(applicationContext)
            AttachmentStagingStore(
                dao = database.attachmentStagingDao(),
                protectedFiles = ProtectedAttachmentFileStore(applicationContext),
            ).reconcileOrphanedFiles()
        }.fold(
            onSuccess = { Result.success() },
            onFailure = {
                // Reconciliation is cleanup, never a reason to destroy local
                // intent. A later process start can retry after transient I/O.
                Result.retry()
            },
        )
    }
}
