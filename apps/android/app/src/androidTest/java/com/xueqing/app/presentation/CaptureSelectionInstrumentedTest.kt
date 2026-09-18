package com.xueqing.app.presentation

import android.content.Context
import androidx.lifecycle.ViewModelStore
import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.xueqing.app.application.bootstrap.PersonalBootstrap
import com.xueqing.app.application.bootstrap.PersonalBootstrapActor
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.bootstrap.PersonalBootstrapUnknownReason
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import com.xueqing.app.durability.DraftDatabase
import com.xueqing.app.durability.DraftStore
import java.time.Instant
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class CaptureSelectionInstrumentedTest {
    private val first = PersonalTeachingContext(
        UUID(0, 1), UUID(0, 2), "虚构学生甲", UUID(0, 3), "chinese", UUID(0, 4),
    )
    private val second = first.copy(studentId = UUID(0, 5), studentDisplayName = "虚构学生乙")

    @Test
    fun selection_blocks_editing_while_previous_autosave_holds_the_mutex() = runBlocking {
        withViewModel(loaded(listOf(first, second))) { vm, database ->
            withContext(Dispatchers.Main) { vm.selectTeachingContext(first) }
            awaitStatus(vm, TeachingContextStatus.Ready)
            val transactionStarted = CountDownLatch(1)
            val releaseTransaction = CountDownLatch(1)
            val blocker = Thread {
                database.runInTransaction {
                    transactionStarted.countDown()
                    check(releaseTransaction.await(10, TimeUnit.SECONDS))
                }
            }
            blocker.start()
            try {
                assertTrue(transactionStarted.await(5, TimeUnit.SECONDS))
                withContext(Dispatchers.Main) {
                    // Autosave owns saveMutex while Room waits for the transaction.
                    vm.onTextChanged("甲的原始记录")
                    vm.selectTeachingContext(second)
                    assertEquals(TeachingContextStatus.Loading, vm.uiState.value.teachingContextStatus)
                    vm.onTextChanged("切换期间不应接受的输入")
                    assertEquals("甲的原始记录", vm.uiState.value.text)
                }
            } finally {
                releaseTransaction.countDown()
                blocker.join(10_000)
            }
            awaitStatus(vm, TeachingContextStatus.Ready)
            assertEquals("虚构学生乙", vm.uiState.value.studentDisplayName)
            assertEquals("", vm.uiState.value.text)
            withContext(Dispatchers.Main) { vm.selectTeachingContext(first) }
            awaitStatus(vm, TeachingContextStatus.Ready)
            assertEquals("甲的原始记录", vm.uiState.value.text)
        }
    }

    @Test
    fun unscoped_capture_preserves_authentication_and_unavailable_states() = runBlocking {
        val cases = listOf(
            PersonalBootstrapResult.AuthenticationRequired to TeachingContextStatus.AuthenticationRequired,
            PersonalBootstrapResult.UnknownResult(PersonalBootstrapUnknownReason.NetworkFailure) to
                TeachingContextStatus.Unavailable,
            loaded(emptyList()) to TeachingContextStatus.Unavailable,
        )
        for ((result, expected) in cases) {
            withViewModel(result) { vm, _ ->
                withContext(Dispatchers.Main) { vm.prepareForUnscopedCapture() }
                assertEquals(expected, vm.uiState.value.teachingContextStatus)
            }
        }
    }

    private fun loaded(contexts: List<PersonalTeachingContext>) = PersonalBootstrapResult.Loaded(
        PersonalBootstrap(
            Instant.EPOCH,
            PersonalBootstrapActor(UUID(0, 6), "虚构教师"),
            emptyList(),
            contexts,
        ),
    )

    private suspend fun awaitStatus(vm: QuickCaptureViewModel, status: TeachingContextStatus) {
        withTimeout(10_000) { vm.uiState.first { it.teachingContextStatus == status } }
    }

    private suspend fun withViewModel(
        result: PersonalBootstrapResult,
        block: suspend (QuickCaptureViewModel, DraftDatabase) -> Unit,
    ) {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val database = Room.inMemoryDatabaseBuilder(context, DraftDatabase::class.java).build()
        val owner = ViewModelStore()
        try {
            val vm = withContext(Dispatchers.Main) {
                QuickCaptureViewModel(
                    DraftStore(database.draftDao()), database.durableIntentDao(),
                    PersonalBootstrapRemote { result }, "selection-test", {},
                ).also { owner.put("capture", it) }
            }
            withTimeout(10_000) {
                vm.uiState.first { it.teachingContextStatus != TeachingContextStatus.Loading }
            }
            block(vm, database)
        } finally {
            withContext(Dispatchers.Main) { owner.clear() }
            database.close()
        }
    }
}
