package com.xueqing.app.durability

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class AttachmentDrainWorker(
    appContext: Context,
    workerParameters: WorkerParameters,
) : CoroutineWorker(appContext, workerParameters) {
    override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
        val drainer = AttachmentSyncRuntime.createDrainer(applicationContext)
            ?: return@withContext Result.success()

        val result = drainer.drainReady()
        result.nextWakeAtEpochMillis?.let { wakeAt ->
            AttachmentOutboxScheduler.scheduleAt(
                context = applicationContext,
                wakeAtEpochMillis = wakeAt,
            )
        }
        Result.success()
    }
}
