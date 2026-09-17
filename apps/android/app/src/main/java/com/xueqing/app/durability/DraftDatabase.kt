package com.xueqing.app.durability

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase

@Database(
    entities = [DraftEntity::class, DraftScopeStateEntity::class],
    version = 1,
    exportSchema = true,
)
abstract class DraftDatabase : RoomDatabase() {
    abstract fun draftDao(): DraftDao

    companion object {
        const val DATABASE_NAME = "xueqing-durable-intent.db"

        @Volatile
        private var instance: DraftDatabase? = null

        fun create(context: Context): DraftDatabase =
            Room.databaseBuilder(
                context.applicationContext,
                DraftDatabase::class.java,
                DATABASE_NAME,
            ).build()

        fun get(context: Context): DraftDatabase =
            instance ?: synchronized(this) {
                instance ?: create(context).also { created -> instance = created }
            }
    }
}
