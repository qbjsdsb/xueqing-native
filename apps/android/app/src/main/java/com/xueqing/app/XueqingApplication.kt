package com.xueqing.app

import android.app.Application
import com.xueqing.app.durability.AttachmentOutboxScheduler
import com.xueqing.app.durability.ObservationOutboxScheduler

class XueqingApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        // Install the build-variant runtime before any worker is kicked. Release
        // composition restores the secure provider session on an IO scope and
        // workers obtain access tokens only through the live process runtime.
        BuildVariantRuntimeHooks.onApplicationCreate(this)

        // Covers the crash window where an Outbox transaction committed but
        // the process died before the immediate WorkManager enqueue executed.
        ObservationOutboxScheduler.kick(this)
        // Covers process death after an authoritative Observation receipt or
        // attachment upload/commit result before the next media enqueue.
        AttachmentOutboxScheduler.kick(this)
    }
}
