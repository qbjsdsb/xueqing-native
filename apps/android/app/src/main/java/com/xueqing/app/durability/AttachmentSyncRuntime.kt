package com.xueqing.app.durability

import android.content.Context

object AttachmentSyncRuntime {
    fun interface DrainerFactory {
        fun create(context: Context): AttachmentOutboxDrainer?
    }

    @Volatile
    private var factory: DrainerFactory? = null

    fun install(factory: DrainerFactory) {
        this.factory = factory
    }

    fun clear() {
        factory = null
    }

    fun isInstalled(): Boolean = factory != null

    fun createDrainer(context: Context): AttachmentOutboxDrainer? =
        factory?.create(context.applicationContext)
}
