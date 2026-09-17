package com.xueqing.app.durability

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import java.io.File
import net.zetetic.database.sqlcipher.SupportOpenHelperFactory

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

        @Volatile
        private var sqlCipherLoaded = false

        fun create(context: Context): DraftDatabase {
            val appContext = context.applicationContext
            val databaseFile = appContext.getDatabasePath(DATABASE_NAME)
            val databaseKey = EncryptedDatabaseKeyManager.obtainDatabaseKey(appContext, databaseFile)
            loadSqlCipher()

            // SQLCipher's Room 2 factory retains the password for the helper's
            // lifetime, so we deliberately make no false "immediate zeroize"
            // claim. The password is never written to disk and disappears with
            // the process / database instance.
            val factory = SupportOpenHelperFactory(databaseKey)
            return Room.databaseBuilder(
                appContext,
                DraftDatabase::class.java,
                databaseFile.absolutePath,
            )
                .openHelperFactory(factory)
                .build()
        }

        fun get(context: Context): DraftDatabase =
            instance ?: synchronized(this) {
                instance ?: create(context).also { created -> instance = created }
            }

        fun purgeLocalEncryptedData(context: Context) {
            val appContext = context.applicationContext
            synchronized(this) {
                instance?.close()
                instance = null
            }

            val databaseFile = appContext.getDatabasePath(DATABASE_NAME)
            appContext.deleteDatabase(DATABASE_NAME)
            EncryptedDatabaseKeyManager.databaseFiles(databaseFile).forEach(File::delete)
            EncryptedDatabaseKeyManager.deleteKeyMaterial(appContext)
        }

        private fun loadSqlCipher() {
            if (sqlCipherLoaded) {
                return
            }
            synchronized(this) {
                if (!sqlCipherLoaded) {
                    System.loadLibrary("sqlcipher")
                    sqlCipherLoaded = true
                }
            }
        }
    }
}
