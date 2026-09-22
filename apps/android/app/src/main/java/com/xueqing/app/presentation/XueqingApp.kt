package com.xueqing.app.presentation

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.xueqing.app.application.session.ClientSessionController
import com.xueqing.app.application.session.ClientSessionStage
import com.xueqing.app.presentation.design.XueqingTheme
import com.xueqing.app.presentation.learning.LearningScreen
import com.xueqing.app.presentation.shell.AppNavigationState
import com.xueqing.app.presentation.shell.PrimaryDestination
import com.xueqing.app.presentation.students.StudentDetailScreen
import com.xueqing.app.presentation.students.StudentsScreen
import com.xueqing.app.presentation.today.TodayScreen
import kotlinx.coroutines.launch

@Composable
internal fun XueqingApp(
    quickCaptureViewModel: QuickCaptureViewModel,
    studentDirectoryViewModel: StudentDirectoryViewModel,
    learningReadViewModel: LearningReadViewModel,
    startInQuickCapture: Boolean = false,
    sessionController: ClientSessionController? = null,
    onSessionBoundaryChanged: () -> Unit = {},
) {
    XueqingTheme {
        if (sessionController != null) {
            val sessionState by sessionController.state.collectAsState()
            if (sessionState.stage != ClientSessionStage.Authenticated) {
                SessionGateScreen(
                    controller = sessionController,
                    stage = sessionState.stage,
                    message = sessionState.message,
                    onSessionBoundaryChanged = onSessionBoundaryChanged,
                )
                return@XueqingTheme
            }
        }

        AuthenticatedWorkspace(
            quickCaptureViewModel = quickCaptureViewModel,
            studentDirectoryViewModel = studentDirectoryViewModel,
            learningReadViewModel = learningReadViewModel,
            startInQuickCapture = startInQuickCapture,
            sessionController = sessionController,
            onSessionBoundaryChanged = onSessionBoundaryChanged,
        )
    }
}

@Composable
private fun AuthenticatedWorkspace(
    quickCaptureViewModel: QuickCaptureViewModel,
    studentDirectoryViewModel: StudentDirectoryViewModel,
    learningReadViewModel: LearningReadViewModel,
    startInQuickCapture: Boolean,
    sessionController: ClientSessionController?,
    onSessionBoundaryChanged: () -> Unit,
) {
    val quickCaptureState by quickCaptureViewModel.uiState.collectAsState()
    val directoryState by studentDirectoryViewModel.uiState.collectAsState()
    val learningState by learningReadViewModel.uiState.collectAsState()
    val scope = rememberCoroutineScope()

    LaunchedEffect(directoryState.actorAppUserId) {
        val actorId = directoryState.actorAppUserId
        if (actorId != null) {
            learningReadViewModel.refreshToday(actorId)
        } else {
            learningReadViewModel.clearAll()
        }
    }

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
            topBar = {
                if (sessionController != null) {
                    ProductionAccountBar(
                        onSignOut = {
                            scope.launch {
                                sessionController.signOut()
                                if (sessionController.state.value.stage ==
                                    ClientSessionStage.SignedOut
                                ) {
                                    onSessionBoundaryChanged()
                                }
                            }
                        },
                    )
                }
            },
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

@Composable
private fun ProductionAccountBar(
    onSignOut: () -> Unit,
) {
    Surface(tonalElevation = 1.dp) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 12.dp, vertical = 4.dp),
            horizontalArrangement = Arrangement.End,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            TextButton(
                onClick = onSignOut,
                modifier = Modifier.testTag("auth-sign-out"),
            ) {
                Text("退出登录")
            }
        }
    }
}

@Composable
private fun SessionGateScreen(
    controller: ClientSessionController,
    stage: ClientSessionStage,
    message: String,
    onSessionBoundaryChanged: () -> Unit,
) {
    val scope = rememberCoroutineScope()
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Column(
            modifier = Modifier.widthIn(max = 420.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(
                text = "学情",
                style = MaterialTheme.typography.headlineMedium,
            )
            Text(
                text = if (message.isBlank()) {
                    "登录后进入你的教学工作区。"
                } else {
                    message
                },
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )

            when (stage) {
                ClientSessionStage.SignedOut -> {
                    OutlinedTextField(
                        value = email,
                        onValueChange = { email = it },
                        label = { Text("邮箱") },
                        singleLine = true,
                        modifier = Modifier
                            .fillMaxWidth()
                            .testTag("auth-email"),
                    )
                    OutlinedTextField(
                        value = password,
                        onValueChange = { password = it },
                        label = { Text("密码") },
                        singleLine = true,
                        visualTransformation = PasswordVisualTransformation(),
                        modifier = Modifier
                            .fillMaxWidth()
                            .testTag("auth-password"),
                    )
                    Button(
                        enabled = email.isNotBlank() && password.isNotBlank(),
                        onClick = {
                            scope.launch {
                                controller.signIn(email.trim(), password)
                                password = ""
                                if (controller.state.value.stage ==
                                    ClientSessionStage.Authenticated
                                ) {
                                    onSessionBoundaryChanged()
                                }
                            }
                        },
                        modifier = Modifier
                            .fillMaxWidth()
                            .testTag("auth-submit"),
                    ) {
                        Text("登录")
                    }
                }

                ClientSessionStage.ReconnectRequired -> {
                    Button(
                        onClick = {
                            scope.launch {
                                controller.retryRestore()
                                if (controller.state.value.stage ==
                                    ClientSessionStage.Authenticated
                                ) {
                                    onSessionBoundaryChanged()
                                }
                            }
                        },
                        modifier = Modifier
                            .fillMaxWidth()
                            .testTag("auth-retry"),
                    ) {
                        Text("重试连接")
                    }
                    TextButton(
                        onClick = {
                            scope.launch {
                                controller.clearLocalSession()
                            }
                        },
                        modifier = Modifier.testTag("auth-clear-session"),
                    ) {
                        Text("退出此设备账号")
                    }
                }

                ClientSessionStage.Restoring,
                ClientSessionStage.Busy,
                -> {
                    CircularProgressIndicator(
                        modifier = Modifier.testTag("auth-progress"),
                    )
                }

                ClientSessionStage.ConfigurationUnavailable -> {
                    Text(
                        text = "请重新启动应用；如果问题持续存在，请安装更新后的正式版本。",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }

                ClientSessionStage.Authenticated -> Unit
            }
        }
    }
}
