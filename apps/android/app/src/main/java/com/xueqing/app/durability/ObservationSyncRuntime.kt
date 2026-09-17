package com.xueqing.app.durability

import android.content.Context

/**
 * Process-local composition seam for WorkManager.
 *
 * The Durable Outbox never stores access tokens. A real session/auth layer can
 * install a factory that builds remotes from the current live session whenever
 * the process starts. Tests and the development/reference provider use the same
 * seam without leaking provider types into the queue or UI.
 */
object ObservationSyncRuntime {
    fun interface DrainerFactory {
        fun create(context: Context): ObservationOutboxDrainer?
    }

    @Volatile
    private var factory: DrainerFactory? = null

    fun install(factory: DrainerFactory) {
        this.factory = factory
    }

    fun clear() {
        factory = null
    }

    fun createDrainer(context: Context): ObservationOutboxDrainer? =
        factory?.create(context.applicationContext)
}
