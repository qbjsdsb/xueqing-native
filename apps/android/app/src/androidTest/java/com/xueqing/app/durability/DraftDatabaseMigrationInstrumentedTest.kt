package com.xueqing.app.durability

import androidx.room.testing.MigrationTestHelper
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class DraftDatabaseMigrationInstrumentedTest {
    @get:Rule
    val helper = MigrationTestHelper(
        InstrumentationRegistry.getInstrumentation(),
        DraftDatabase::class.java,
    )

    @Test
    fun migration1To2PreservesDraftAndAddsEmptyOutbox() {
        helper.createDatabase(TEST_DB, 1).apply {
            execSQL(
                "INSERT INTO draft_scope_state(scope_key, epoch) VALUES(?, ?)",
                arrayOf<Any>(SCOPE_KEY, 7L),
            )
            execSQL(
                "INSERT INTO drafts(scope_key, epoch, text, updated_at_epoch_millis) VALUES(?, ?, ?, ?)",
                arrayOf<Any>(SCOPE_KEY, 7L, SENTINEL, 1_789_632_000_000L),
            )
            close()
        }

        val migrated = helper.runMigrationsAndValidate(
            TEST_DB,
            2,
            true,
            DraftDatabase.MIGRATION_1_2,
        )

        migrated.query("SELECT epoch, text FROM drafts WHERE scope_key = ?", arrayOf<Any>(SCOPE_KEY)).use { cursor ->
            assertTrue(cursor.moveToFirst())
            assertEquals(7L, cursor.getLong(0))
            assertEquals(SENTINEL, cursor.getString(1))
        }
        migrated.query("SELECT COUNT(*) FROM observation_outbox").use { cursor ->
            assertTrue(cursor.moveToFirst())
            assertEquals(0L, cursor.getLong(0))
        }
        migrated.close()
    }

    @Test
    fun migration2To3PreservesDraftAndAddsEmptyAttachmentStaging() {
        helper.createDatabase(TEST_DB_V2, 2).apply {
            execSQL(
                "INSERT INTO draft_scope_state(scope_key, epoch) VALUES(?, ?)",
                arrayOf<Any>(SCOPE_KEY, 9L),
            )
            execSQL(
                "INSERT INTO drafts(scope_key, epoch, text, updated_at_epoch_millis) VALUES(?, ?, ?, ?)",
                arrayOf<Any>(SCOPE_KEY, 9L, V2_SENTINEL, 1_789_632_000_100L),
            )
            close()
        }

        val migrated = helper.runMigrationsAndValidate(
            TEST_DB_V2,
            3,
            true,
            DraftDatabase.MIGRATION_2_3,
        )

        migrated.query("SELECT epoch, text FROM drafts WHERE scope_key = ?", arrayOf<Any>(SCOPE_KEY)).use { cursor ->
            assertTrue(cursor.moveToFirst())
            assertEquals(9L, cursor.getLong(0))
            assertEquals(V2_SENTINEL, cursor.getString(1))
        }
        migrated.query("SELECT COUNT(*) FROM attachment_staging").use { cursor ->
            assertTrue(cursor.moveToFirst())
            assertEquals(0L, cursor.getLong(0))
        }
        migrated.close()
    }

    private companion object {
        const val TEST_DB = "xueqing-migration-test"
        const val TEST_DB_V2 = "xueqing-migration-v2-v3-test"
        const val SCOPE_KEY = "migration-scope"
        const val SENTINEL = "v1 草稿必须在 v2 Outbox 迁移后保留"
        const val V2_SENTINEL = "v2 Durable Intent 必须在 v3 附件 staging 迁移后保留"
    }
}
