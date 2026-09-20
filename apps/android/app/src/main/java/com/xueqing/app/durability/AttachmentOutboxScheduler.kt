package com.xueqing.app.durability

import android.content.Context
import androidx.work.Constraints
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import java.util.concurrent.TimeUnit

object AttachmentOutboxScheduler {
    private const val IMMEDIATE_WORK_NAME = "xueqing-attachment-drain-now"
    private const val RETRY_WORK_NAME = "xueqing-attachment-drain-retry"

    private val networkConstraints = Constraints.Builder()
        .setRequiredNetworkType(NetworkType.CONNECTED)
        .build()

    fun kick(context: Context) {
        val request = OneTimeWorkRequestBuilder<AttachmentDrainWorker>()
            .setConstraints(networkConstraints)
            .build()
        WorkManager.getInstance(context.applicationContext).enqueueUniqueWork(
            IMMEDIATE_WORK_NAME,
            ExistingWorkPolicy.KEEP,
            request,
        )
    }

    fun scheduleAt(
        context: Context,
        wakeAtEpochMillis: Long,
        nowEpochMillis: Long = System.currentTimeMillis(),
    ) {
        val delayMillis = (wakeAtEpochMillis - nowEpochMillis).coerceAtLeast(0)
        val request = OneTimeWorkRequestBuilder<AttachmentDrainWorker>()
            .setConstraints(networkConstraints)
            .setInitialDelay(delayMillis, TimeUnit.MILLISECONDS)
            .build()
        WorkManager.getInstance(context.applicationContext).enqueueUniqueWork(
            RETRY_WORK_NAME,
            ExistingWorkPolicy.REPLACE,
            request,
        )
    }
}
