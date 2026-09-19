package com.xueqing.app.presentation

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import com.xueqing.app.application.learning.StudentLearningCaseFocus
import com.xueqing.app.presentation.design.EmptyMessage
import com.xueqing.app.presentation.design.SectionTitle
import com.xueqing.app.presentation.design.StatusMessage
import com.xueqing.app.presentation.design.XueqingTheme
import com.xueqing.app.presentation.design.caseStateLabel
import com.xueqing.app.presentation.design.dueLabel
import com.xueqing.app.presentation.shell.AppNavigationState
import com.xueqing.app.presentation.shell.PrimaryDestination
import com.xueqing.app.presentation.students.StudentRow
import com.xueqing.app.presentation.students.studentStorageKey
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

@Composable
private fun StudentsScreen(
    innerPadding: PaddingValues,
    state: StudentDirectoryUiState,
    onOpenStudent: (String) -> Unit,
) {
    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .padding(innerPadding),
        contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp),
    ) {
        item {
            Text(
                text = "学生",
                style = MaterialTheme.typography.headlineSmall,
                modifier = Modifier.padding(bottom = 8.dp),
            )
            Text(
                text = "按当前个人教学范围查看学生与学科档案。",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(bottom = 20.dp),
            )
        }

        when (state.status) {
            StudentDirectoryStatus.Loading -> item {
                StatusMessage("正在读取学生…")
            }

            StudentDirectoryStatus.AuthenticationRequired -> item {
                EmptyMessage("请登录后查看学生。")
            }

            StudentDirectoryStatus.Unavailable -> item {
                EmptyMessage("暂时无法读取学生列表，请稍后再试。")
            }

            StudentDirectoryStatus.Ready -> {
                if (state.students.isEmpty()) {
                    item { EmptyMessage("还没有可用的教学学生。") }
                } else {
                    items(state.students, key = { it.key }) { student ->
                        StudentRow(
                            student = student,
                            onClick = { onOpenStudent(student.key) },
                            testTag = "student-row-" + student.key,
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun StudentDetailScreen(
    student: StudentDirectoryItem,
    actorAppUserId: java.util.UUID?,
    focusState: FocusLearningUiState,
    onBack: () -> Unit,
    onLoadFocus: (PersonalTeachingContext) -> Unit,
    onQuickCapture: (PersonalTeachingContext) -> Unit,
) {
    BackHandler(onBack = onBack)
    var selectedFocusProfileId by rememberSaveable(student.key) {
        mutableStateOf<String?>(null)
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp),
    ) {
        item {
            TextButton(onClick = onBack) {
                Text("返回学生")
            }
            Text(
                text = "学生详情",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 8.dp),
            )
            Text(
                text = student.studentDisplayName,
                style = MaterialTheme.typography.headlineSmall,
                modifier = Modifier.padding(top = 4.dp),
            )
            Text(
                text = student.organizationName,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp, bottom = 28.dp),
            )
        }

        item { SectionTitle("学科档案") }

        items(
            items = student.contexts,
            key = { it.subjectProfileId.toString() },
        ) { context ->
            Column {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = subjectLabelForKey(context.subjectKey),
                            style = MaterialTheme.typography.titleMedium,
                        )
                        Text(
                            text = "当前关注与课堂记录",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.padding(top = 4.dp),
                        )
                    }
                    TextButton(
                        enabled = actorAppUserId != null,
                        onClick = {
                            selectedFocusProfileId = context.subjectProfileId.toString()
                            onLoadFocus(context)
                        },
                        modifier = Modifier.testTag(
                            "student-subject-focus-" + context.subjectProfileId,
                        ),
                    ) {
                        Text("关注")
                    }
                    TextButton(
                        onClick = { onQuickCapture(context) },
                        modifier = Modifier.testTag(
                            "student-subject-record-" + context.subjectProfileId,
                        ),
                    ) {
                        Text("记录")
                    }
                }

                if (selectedFocusProfileId == context.subjectProfileId.toString()) {
                    CurrentFocusInline(
                        state = focusState,
                        context = context,
                    )
                }

                HorizontalDivider()
            }
        }

        item {
            Text(
                text = "记录提交与当前关注读取都会由服务端重新检查当前任课关系。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 20.dp),
            )
        }
    }
}

@Composable
private fun CurrentFocusInline(
    state: FocusLearningUiState,
    context: PersonalTeachingContext,
) {
    val matches = state.scope?.let { scope ->
        scope.organizationId == context.organizationId &&
            scope.studentId == context.studentId &&
            scope.subjectProfileId == context.subjectProfileId &&
            state.ownerAssignmentId == context.assignmentId
    } == true

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 8.dp, end = 8.dp, bottom = 12.dp),
    ) {
        Text(
            text = "当前关注",
            style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(bottom = 4.dp),
        )

        if (!matches || state.status == LearningReadStatus.Loading) {
            StatusMessage("正在读取当前关注…")
        } else {
            when (state.status) {
                LearningReadStatus.Empty ->
                    EmptyMessage("当前学科暂无持续跟进的问题。")

                LearningReadStatus.Data -> {
                    state.snapshot?.cases.orEmpty().forEach { learningCase ->
                        CurrentFocusRow(learningCase)
                    }
                    if (state.snapshot?.hasMore == true) {
                        StatusMessage("仅显示最近更新的 3 个关注项。")
                    }
                }

                LearningReadStatus.AuthenticationRequired ->
                    EmptyMessage("登录状态已失效，未显示旧关注项。")

                LearningReadStatus.AccessDenied ->
                    EmptyMessage("当前任课关系已变化，未显示旧关注项。")

                LearningReadStatus.ServerInvariant ->
                    EmptyMessage("当前学情数据需要服务器校验，暂不展示。")

                LearningReadStatus.TransientFailure ->
                    EmptyMessage("暂时无法刷新当前关注，可稍后再试。")

                LearningReadStatus.ProtocolFailure ->
                    EmptyMessage("服务器返回无法验证，已拒绝显示关注项。")

                LearningReadStatus.Idle,
                LearningReadStatus.Loading,
                -> StatusMessage("正在读取当前关注…")
            }
        }
    }
}

@Composable
private fun CurrentFocusRow(
    learningCase: StudentLearningCaseFocus,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .testTag("current-focus-" + learningCase.caseId)
            .padding(vertical = 10.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = learningCase.title,
                style = MaterialTheme.typography.titleSmall,
                modifier = Modifier
                    .weight(1f)
                    .padding(end = 12.dp),
            )
            Text(
                text = caseStateLabel(learningCase.state),
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        Text(
            text = learningCase.primaryAction.actionText,
            style = MaterialTheme.typography.bodyMedium,
            modifier = Modifier.padding(top = 5.dp),
        )
        Text(
            text = dueLabel(
                learningCase.primaryAction.dueBucket,
                learningCase.primaryAction.dueOn,
            ),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(top = 3.dp),
        )
    }
}

@Composable
private fun LearningScreen(
    innerPadding: PaddingValues,
    focusState: FocusLearningUiState,
    onOpenStudent: (String) -> Unit,
    onOpenStudents: () -> Unit,
) {
    val snapshot = focusState.snapshot
    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .padding(innerPadding),
        contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp),
    ) {
        item {
            Text(
                text = "学情",
                style = MaterialTheme.typography.headlineSmall,
            )
        }

        if (focusState.status == LearningReadStatus.Data && snapshot != null) {
            item {
                SectionTitle(
                    text = "最近查看",
                    modifier = Modifier.padding(top = 24.dp),
                )
                Text(
                    text = snapshot.studentDisplayName + " · " +
                        subjectLabelForKey(snapshot.subjectKey),
                    style = MaterialTheme.typography.titleMedium,
                )
            }
            items(snapshot.cases, key = { it.caseId.toString() }) { learningCase ->
                CurrentFocusRow(learningCase)
                HorizontalDivider()
            }
            item {
                TextButton(
                    onClick = {
                        onOpenStudent(
                            studentStorageKey(
                                snapshot.organizationId.toString(),
                                snapshot.studentId.toString(),
                            ),
                        )
                    },
                    modifier = Modifier.padding(top = 8.dp),
                ) {
                    Text("查看学生")
                }
            }
        } else {
            item {
                Text(
                    text = "从学生进入具体学科，查看服务器确认的当前关注与下一步行动。",
                    style = MaterialTheme.typography.bodyLarge,
                    modifier = Modifier.padding(top = 24.dp),
                )
                TextButton(
                    onClick = onOpenStudents,
                    modifier = Modifier.padding(top = 8.dp),
                ) {
                    Text("查看学生")
                }
            }
        }
    }
}
