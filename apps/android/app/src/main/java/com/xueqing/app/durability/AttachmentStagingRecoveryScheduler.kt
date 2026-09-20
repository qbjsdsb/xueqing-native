package com.xueqing.app.durability

import android.content.Context
import androidx.work.ExistingWorkPolicy
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import java.util.concurrent.TimeUnit

object AttachmentStagingRecoveryScheduler {
    private const val WORK_NAME = "xueqing-attachment-staging-reconcile"
    private const val DELAYED_WORK_NAME = "xueqing-attachment-staging-reconcile-delayed"

    fun kick(context: Context) {
        val request = OneTimeWorkRequestBuilder<AttachmentStagingRecoveryWorker>()
            .build()
        WorkManager.getInstance(context.applicationContext).enqueueUniqueWork(
            WORK_NAME,
            ExistingWorkPolicy.KEEP,
            request,
        )
    }

    fun scheduleAfterGrace(context: Context) {
        val request = OneTimeWorkRequestBuilder<AttachmentStagingRecoveryWorker>()
            .setInitialDelay(
                AttachmentStagingStore.DEFAULT_RECONCILIATION_GRACE_MILLIS,
                TimeUnit.MILLISECONDS,
            )
            .build()
        WorkManager.getInstance(context.applicationContext).enqueueUniqueWork(
            DELAYED_WORK_NAME,
            ExistingWorkPolicy.KEEP,
            request,
        )
    }
}
