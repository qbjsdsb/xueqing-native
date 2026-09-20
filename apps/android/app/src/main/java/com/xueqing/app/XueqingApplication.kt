package com.xueqing.app

import android.app.Application
import com.xueqing.app.durability.AttachmentOutboxScheduler
import com.xueqing.app.durability.ObservationOutboxScheduler

class XueqingApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        // Covers the crash window where an Outbox transaction committed but
        // the process died before the immediate WorkManager enqueue executed.
        ObservationOutboxScheduler.kick(this)
        // Covers process death after an authoritative Observation receipt or
        // attachment upload/commit result before the next media enqueue.
        AttachmentOutboxScheduler.kick(this)
    }
}
