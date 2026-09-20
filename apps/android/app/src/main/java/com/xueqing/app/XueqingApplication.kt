package com.xueqing.app

import android.app.Application
import com.xueqing.app.durability.AttachmentStagingRecoveryScheduler
import com.xueqing.app.durability.ObservationOutboxScheduler

class XueqingApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        // Covers the crash window where an Outbox transaction committed but
        // the process died before the immediate WorkManager enqueue executed.
        ObservationOutboxScheduler.kick(this)
        // Reconcile only stale, unreferenced protected attachment files. The
        // worker reads Room metadata before deleting anything and keeps a grace
        // window so it cannot race a newly staged attachment.
        AttachmentStagingRecoveryScheduler.kick(this)
    }
}
