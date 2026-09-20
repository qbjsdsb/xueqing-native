package com.xueqing.app.durability

import android.content.Context
import androidx.work.ExistingWorkPolicy
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager

object AttachmentStagingRecoveryScheduler {
    private const val WORK_NAME = "xueqing-attachment-staging-reconcile"

    fun kick(context: Context) {
        val request = OneTimeWorkRequestBuilder<AttachmentStagingRecoveryWorker>()
            .build()
        WorkManager.getInstance(context.applicationContext).enqueueUniqueWork(
            WORK_NAME,
            ExistingWorkPolicy.KEEP,
            request,
        )
    }
}
