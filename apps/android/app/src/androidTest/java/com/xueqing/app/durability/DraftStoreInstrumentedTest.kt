package com.xueqing.app.durability

import android.content.Context
import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class DraftStoreInstrumentedTest {
    private lateinit var database: DraftDatabase
    private lateinit var store: DraftStore

    private val baseScope = DraftScope(
        environmentId = "ci",
        appUserId = "user-a",
        organizationId = "org-a",
        studentId = "student-a",
        subjectId = "subject-chinese",
        contextId = "quick-capture",
    )

    @Before
    fun setUp() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        database = Room.inMemoryDatabaseBuilder(context, DraftDatabase::class.java)
            .allowMainThreadQueries()
            .build()
        store = DraftStore(database.draftDao()) { 1_789_632_000_000L }
    }

    @After
    fun tearDown() {
        database.close()
    }

    @Test
    fun savedDraftReopensInSameScope() = runBlocking {
        val session = store.open(baseScope)
        assertTrue(store.save(session, "课堂观察：概括题仍有原句搬运。"))

        val reopened = store.open(baseScope)
        assertEquals("课堂观察：概括题仍有原句搬运。", reopened.recovered?.text)
        assertEquals(session.epoch, reopened.epoch)
    }

    @Test
    fun discardAdvancesEpochAndBlocksDelayedStaleAutosave() = runBlocking {
        val session = store.open(baseScope)
        assertTrue(store.save(session, "准备丢弃的草稿"))

        val nextEpoch = store.discard(session)
        assertEquals(session.epoch + 1, nextEpoch)
        assertNull(store.load(baseScope))

        val staleAutosaveAccepted = store.save(session, "延迟到达的旧 autosave")
        assertFalse(staleAutosaveAccepted)
        assertNull(store.load(baseScope))

        val reopened = store.open(baseScope)
        assertEquals(nextEpoch, reopened.epoch)
        assertNull(reopened.recovered)
    }

    @Test
    fun scopeIsolationSeparatesUserOrganizationStudentAndSubject() = runBlocking {
        val original = store.open(baseScope)
        assertTrue(store.save(original, "只属于原始 scope 的草稿"))

        val variants = listOf(
            baseScope.copy(appUserId = "user-b"),
            baseScope.copy(organizationId = "org-b"),
            baseScope.copy(studentId = "student-b"),
            baseScope.copy(subjectId = "subject-math"),
            baseScope.copy(contextId = "case-note"),
        )

        variants.forEach { variant ->
            assertNull("Draft leaked into $variant", store.open(variant).recovered)
        }

        assertEquals("只属于原始 scope 的草稿", store.open(baseScope).recovered?.text)
    }
}
