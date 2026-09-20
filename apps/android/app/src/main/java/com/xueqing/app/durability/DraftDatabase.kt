package com.xueqing.app.durability

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import java.io.File
import net.zetetic.database.sqlcipher.SupportOpenHelperFactory

@Database(
    entities = [
        DraftEntity::class,
        DraftScopeStateEntity::class,
        ObservationOutboxEntity::class,
        AttachmentStagingEntity::class,
    ],
    version = 3,
    exportSchema = true,
)
abstract class DraftDatabase : RoomDatabase() {
    abstract fun draftDao(): DraftDao
    abstract fun durableIntentDao(): DurableIntentDao
    abstract fun attachmentStagingDao(): AttachmentStagingDao

    companion object {
        const val DATABASE_NAME = "xueqing-durable-intent.db"

        val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL(
                    """
                    CREATE TABLE IF NOT EXISTS `observation_outbox` (
                        `local_sequence` INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                        `operation_id` TEXT NOT NULL,
                        `command_type` TEXT NOT NULL,
                        `scope_key` TEXT NOT NULL,
                        `environment_id` TEXT NOT NULL,
                        `app_user_id` TEXT NOT NULL,
                        `organization_id` TEXT NOT NULL,
                        `payload_json` TEXT NOT NULL,
                        `queue_status` TEXT NOT NULL,
                        `attempt_count` INTEGER NOT NULL,
                        `last_error_class` TEXT,
                        `next_attempt_at_epoch_millis` INTEGER NOT NULL,
                        `lease_id` TEXT,
                        `lease_expires_at_epoch_millis` INTEGER,
                        `acknowledged_at_epoch_millis` INTEGER,
                        `server_receipt_json` TEXT,
                        `created_at_epoch_millis` INTEGER NOT NULL
                    )
                    """.trimIndent(),
                )
                db.execSQL(
                    "CREATE UNIQUE INDEX IF NOT EXISTS `index_observation_outbox_operation_id` ON `observation_outbox` (`operation_id`)",
                )
                db.execSQL(
                    "CREATE INDEX IF NOT EXISTS `index_observation_outbox_queue_status_next_attempt_at_epoch_millis` ON `observation_outbox` (`queue_status`, `next_attempt_at_epoch_millis`)",
                )
                db.execSQL(
                    "CREATE INDEX IF NOT EXISTS `index_observation_outbox_environment_id_app_user_id_local_sequence` ON `observation_outbox` (`environment_id`, `app_user_id`, `local_sequence`)",
                )
                db.execSQL(
                    "CREATE INDEX IF NOT EXISTS `index_observation_outbox_scope_key` ON `observation_outbox` (`scope_key`)",
                )
            }
        }

        val MIGRATION_2_3 = object : Migration(2, 3) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL(
                    """
                    CREATE TABLE IF NOT EXISTS `attachment_staging` (
                        `attachment_id` TEXT NOT NULL,
                        `scope_key` TEXT NOT NULL,
                        `draft_epoch` INTEGER NOT NULL,
                        `environment_id` TEXT NOT NULL,
                        `app_user_id` TEXT NOT NULL,
                        `organization_id` TEXT NOT NULL,
                        `student_id` TEXT NOT NULL,
                        `subject_profile_id` TEXT NOT NULL,
                        `assignment_id` TEXT NOT NULL,
                        `local_encrypted_file_name` TEXT NOT NULL,
                        `content_type` TEXT NOT NULL,
                        `byte_size` INTEGER NOT NULL,
                        `state` TEXT NOT NULL,
                        `parent_observation_operation_id` TEXT,
                        `authoritative_observation_id` TEXT,
                        `remote_object_name` TEXT,
                        `attachment_commit_operation_id` TEXT,
                        `last_error_class` TEXT,
                        `created_at_epoch_millis` INTEGER NOT NULL,
                        `updated_at_epoch_millis` INTEGER NOT NULL,
                        PRIMARY KEY(`attachment_id`)
                    )
                    """.trimIndent(),
                )
                db.execSQL("CREATE INDEX IF NOT EXISTS `index_attachment_staging_scope_key_draft_epoch` ON `attachment_staging` (`scope_key`, `draft_epoch`)")
                db.execSQL("CREATE INDEX IF NOT EXISTS `index_attachment_staging_state_updated_at_epoch_millis` ON `attachment_staging` (`state`, `updated_at_epoch_millis`)")
                db.execSQL("CREATE INDEX IF NOT EXISTS `index_attachment_staging_parent_observation_operation_id` ON `attachment_staging` (`parent_observation_operation_id`)")
                db.execSQL("CREATE INDEX IF NOT EXISTS `index_attachment_staging_environment_id_app_user_id_state` ON `attachment_staging` (`environment_id`, `app_user_id`, `state`)")
                db.execSQL("CREATE UNIQUE INDEX IF NOT EXISTS `index_attachment_staging_local_encrypted_file_name` ON `attachment_staging` (`local_encrypted_file_name`)")
            }
        }

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
                .addMigrations(MIGRATION_1_2, MIGRATION_2_3)
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
            ProtectedAttachmentFileStore.purgeAll(appContext)
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
