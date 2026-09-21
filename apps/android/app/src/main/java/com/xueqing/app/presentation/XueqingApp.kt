package com.xueqing.app.presentation

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import com.xueqing.app.presentation.design.XueqingTheme
import com.xueqing.app.presentation.learning.LearningScreen
import com.xueqing.app.presentation.shell.AppNavigationState
import com.xueqing.app.presentation.shell.PrimaryDestination
import com.xueqing.app.presentation.students.StudentDetailScreen
import com.xueqing.app.presentation.students.StudentsScreen
import com.xueqing.app.presentation.today.TodayScreen

@Composable
internal fun XueqingApp(
    quickCaptureViewModel: QuickCaptureViewModel,
    studentDirectoryViewModel: StudentDirectoryViewModel,
    learningReadViewModel: LearningReadViewModel,
    startInQuickCapture: Boolean = false,
) {
    val quickCaptureState by quickCaptureViewModel.uiState.collectAsState()
    val directoryState by studentDirectoryViewModel.uiState.collectAsState()
    val learningState by learningReadViewModel.uiState.collectAsState()

    LaunchedEffect(directoryState.actorAppUserId) {
        val actorId = directoryState.actorAppUserId
        if (actorId != null) {
            learningReadViewModel.refreshToday(actorId)
        } else {
            learningReadViewModel.clearAll()
        }
    }

    XueqingTheme {
        Surface(
            modifier = Modifier.fillMaxSize(),
            color = MaterialTheme.colorScheme.background,
        ) {
            val navigation = AppNavigationState.remember(startInQuickCapture)
        val selectedStudent = directoryState.students.firstOrNull {
            it.key == navigation.selectedStudentKey
        }

        if (navigation.isQuickCaptureOpen) {
            QuickCaptureScreen(
                state = quickCaptureState,
                onTextChanged = quickCaptureViewModel::onTextChanged,
                onClose = {
                    quickCaptureViewModel.flushNow()
                    navigation.closeQuickCapture()
                },
                onChooseStudent = navigation::chooseStudentFromQuickCapture,
                onPhotoSelected = quickCaptureViewModel::onPhotoSelected,
                onRemovePhoto = quickCaptureViewModel::removePhoto,
                onDiscard = quickCaptureViewModel::discard,
                onSubmit = quickCaptureViewModel::submit,
            )
        } else if (selectedStudent != null) {
            StudentDetailScreen(
                student = selectedStudent,
                actorAppUserId = directoryState.actorAppUserId,
                focusState = learningState.focus,
                onBack = navigation::closeStudent,
                onLoadFocus = { context ->
                    directoryState.actorAppUserId?.let { actorId ->
                        learningReadViewModel.loadFocus(context, actorId)
                    }
                },
                onQuickCapture = { context ->
                    quickCaptureViewModel.selectTeachingContext(context)
                    navigation.openQuickCapture()
                },
            )
        } else {
            Scaffold(
                bottomBar = {
                    NavigationBar {
                        PrimaryDestination.entries.forEach { destination ->
                            NavigationBarItem(
                                selected = navigation.destination == destination,
                                onClick = { navigation.selectDestination(destination) },
                                icon = {
                                    Text(
                                        text = destination.bootstrapGlyph,
                                        style = MaterialTheme.typography.labelMedium,
                                    )
                                },
                                label = { Text(destination.label) },
                            )
                        }
                    }
                },
            ) { innerPadding ->
                when (navigation.destination) {
                    PrimaryDestination.Today -> TodayScreen(
                        innerPadding = innerPadding,
                        students = directoryState.students,
                        directoryStatus = directoryState.status,
                        todayState = learningState.today,
                        onQuickCapture = {
                            quickCaptureViewModel.prepareForUnscopedCapture()
                            navigation.openQuickCapture()
                        },
                        onOpenStudent = navigation::openStudent,
                    )

                    PrimaryDestination.Students -> StudentsScreen(
                        innerPadding = innerPadding,
                        state = directoryState,
                        onOpenStudent = navigation::openStudent,
                    )

                    PrimaryDestination.Learning -> LearningScreen(
                        innerPadding = innerPadding,
                        focusState = learningState.focus,
                        onOpenStudent = navigation::openStudent,
                        onOpenStudents = {
                            navigation.selectDestination(PrimaryDestination.Students)
                        },
                    )
                }
            }
        }
        }
    }
}
