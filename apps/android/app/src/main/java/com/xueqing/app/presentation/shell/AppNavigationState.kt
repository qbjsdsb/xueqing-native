package com.xueqing.app.presentation.shell

import androidx.compose.runtime.Composable
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.remember
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable

/**
 * Owns only shell navigation state.
 *
 * Feature screens receive state/events and do not need to know how navigation
 * is stored. The backing values remain rememberSaveable so process recreation
 * keeps the same behavior as the previous inline implementation.
 */
internal class AppNavigationState private constructor(
    private val destinationName: MutableState<String>,
    private val studentKey: MutableState<String?>,
    private val quickCaptureOpen: MutableState<Boolean>,
) {
    val destination: PrimaryDestination
        get() = PrimaryDestination.entries.firstOrNull { it.name == destinationName.value }
            ?: PrimaryDestination.Today

    val selectedStudentKey: String?
        get() = studentKey.value

    val isQuickCaptureOpen: Boolean
        get() = quickCaptureOpen.value

    fun selectDestination(destination: PrimaryDestination) {
        destinationName.value = destination.name
        studentKey.value = null
    }

    fun openStudent(key: String) {
        destinationName.value = PrimaryDestination.Students.name
        studentKey.value = key
    }

    fun closeStudent() {
        studentKey.value = null
    }

    fun openQuickCapture() {
        quickCaptureOpen.value = true
    }

    fun closeQuickCapture() {
        quickCaptureOpen.value = false
    }

    fun chooseStudentFromQuickCapture() {
        quickCaptureOpen.value = false
        destinationName.value = PrimaryDestination.Students.name
        studentKey.value = null
    }

    companion object {
        @Composable
        fun remember(startInQuickCapture: Boolean): AppNavigationState {
            val destinationName = rememberSaveable {
                mutableStateOf(PrimaryDestination.Today.name)
            }
            val studentKey = rememberSaveable {
                mutableStateOf<String?>(null)
            }
            val quickCaptureOpen = rememberSaveable {
                mutableStateOf(startInQuickCapture)
            }

            return remember(destinationName, studentKey, quickCaptureOpen) {
                AppNavigationState(
                    destinationName = destinationName,
                    studentKey = studentKey,
                    quickCaptureOpen = quickCaptureOpen,
                )
            }
        }
    }
}
