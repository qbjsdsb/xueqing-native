package com.xueqing.app.presentation

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.clickable
import androidx.compose.foundation.isSystemInDarkTheme
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
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
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
import com.xueqing.app.application.learning.ActionDueBucket
import com.xueqing.app.application.learning.LearningCaseState
import com.xueqing.app.application.learning.PersonalTodayAction
import com.xueqing.app.application.learning.StudentLearningCaseFocus

internal enum class PrimaryDestination(
    val label: String,
    val bootstrapGlyph: String,
) {
    Today("今日", "今"),
    Students("学生", "生"),
    Learning("学情", "学"),
}

@Composable
internal fun XueqingApp(
    quickCaptureViewModel: QuickCaptureViewModel,
    studentDirectoryViewModel: StudentDirectoryViewModel,
    learningReadViewModel: LearningReadViewModel,
    startInQuickCapture: Boolean = false,
) {
    val colorScheme = if (isSystemInDarkTheme()) darkColorScheme() else lightColorScheme()
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

    MaterialTheme(colorScheme = colorScheme) {
        var selectedName by rememberSaveable { mutableStateOf(PrimaryDestination.Today.name) }
        var selectedStudentKey by rememberSaveable { mutableStateOf<String?>(null) }
        var showingQuickCapture by rememberSaveable { mutableStateOf(startInQuickCapture) }
        val selected = PrimaryDestination.entries.firstOrNull { it.name == selectedName }
            ?: PrimaryDestination.Today
        val selectedStudent = directoryState.students.firstOrNull { it.key == selectedStudentKey }

        if (showingQuickCapture) {
            QuickCaptureScreen(
                state = quickCaptureState,
                onTextChanged = quickCaptureViewModel::onTextChanged,
                onClose = {
                    quickCaptureViewModel.flushNow()
                    showingQuickCapture = false
                },
                onChooseStudent = {
                    showingQuickCapture = false
                    selectedName = PrimaryDestination.Students.name
                    selectedStudentKey = null
                },
                onDiscard = quickCaptureViewModel::discard,
                onSubmit = quickCaptureViewModel::submit,
            )
        } else if (selectedStudent != null) {
            StudentDetailScreen(
                student = selectedStudent,
                actorAppUserId = directoryState.actorAppUserId,
                focusState = learningState.focus,
                onBack = { selectedStudentKey = null },
                onLoadFocus = { context ->
                    directoryState.actorAppUserId?.let { actorId ->
                        learningReadViewModel.loadFocus(context, actorId)
                    }
                },
                onQuickCapture = { context ->
                    quickCaptureViewModel.selectTeachingContext(context)
                    showingQuickCapture = true
                },
            )
        } else {
            Scaffold(
                bottomBar = {
                    NavigationBar {
                        PrimaryDestination.entries.forEach { destination ->
                            NavigationBarItem(
                                selected = selected == destination,
                                onClick = {
                                    selectedName = destination.name
                                    selectedStudentKey = null
                                },
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
                when (selected) {
                    PrimaryDestination.Today -> TodayScreen(
                        innerPadding = innerPadding,
                        students = directoryState.students,
                        directoryStatus = directoryState.status,
                        todayState = learningState.today,
                        onQuickCapture = {
                            quickCaptureViewModel.prepareForUnscopedCapture()
                            showingQuickCapture = true
                        },
                        onOpenStudent = { key ->
                            selectedName = PrimaryDestination.Students.name
                            selectedStudentKey = key
                        },
                    )

                    PrimaryDestination.Students -> StudentsScreen(
                        innerPadding = innerPadding,
                        state = directoryState,
                        onOpenStudent = { selectedStudentKey = it },
                    )

                    PrimaryDestination.Learning -> LearningScreen(
                        innerPadding = innerPadding,
                        focusState = learningState.focus,
                        onOpenStudent = { key ->
                            selectedName = PrimaryDestination.Students.name
                            selectedStudentKey = key
                        },
                        onOpenStudents = {
                            selectedName = PrimaryDestination.Students.name
                        },
                    )
                }
            }
        }
    }
}

@Composable
private fun TodayScreen(
    innerPadding: PaddingValues,
    students: List<StudentDirectoryItem>,
    directoryStatus: StudentDirectoryStatus,
    todayState: TodayLearningUiState,
    onQuickCapture: () -> Unit,
    onOpenStudent: (String) -> Unit,
) {
    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .padding(innerPadding),
        contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp),
    ) {
        item {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text("今日", style = MaterialTheme.typography.headlineSmall)
                TextButton(
                    onClick = onQuickCapture,
                    modifier = Modifier.testTag("today-quick-capture"),
                ) {
                    Text("记录")
                }
            }
        }

        item {
            SectionTitle(
                text = "待办行动",
                modifier = Modifier.padding(top = 20.dp),
            )
        }

        when (todayState.status) {
            LearningReadStatus.Idle,
            LearningReadStatus.Loading,
            -> item { StatusMessage("正在读取今日行动…") }

            LearningReadStatus.Empty -> item {
                EmptyMessage("当前没有待办教学行动。")
            }

            LearningReadStatus.Data -> {
                val actions = todayState.snapshot?.actions.orEmpty()
                items(actions, key = { it.actionId.toString() }) { action ->
                    TodayActionRow(
                        action = action,
                        onClick = {
                            onOpenStudent(
                                studentStorageKey(
                                    action.organizationId.toString(),
                                    action.studentId.toString(),
                                ),
                            )
                        },
                    )
                }
                if (todayState.snapshot?.hasMore == true) {
                    item { StatusMessage("仅显示前 200 条待办行动。") }
                }
            }

            LearningReadStatus.AuthenticationRequired -> item {
                EmptyMessage("请登录后查看今日行动。")
            }

            LearningReadStatus.AccessDenied -> item {
                EmptyMessage("当前教学权限已变化，未显示旧行动。")
            }

            LearningReadStatus.ServerInvariant -> item {
                EmptyMessage("行动数据需要服务器校验，暂不展示。")
            }

            LearningReadStatus.TransientFailure -> item {
                EmptyMessage("暂时无法刷新今日行动，可稍后再试。")
            }

            LearningReadStatus.ProtocolFailure -> item {
                EmptyMessage("服务器返回无法验证，已拒绝显示行动。")
            }
        }

        item {
            SectionTitle(
                text = "我的学生",
                modifier = Modifier.padding(top = 28.dp),
            )
        }

        when (directoryStatus) {
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
                if (students.isEmpty()) {
                    item { EmptyMessage("还没有可用的教学学生。") }
                } else {
                    items(
                        items = students.take(6),
                        key = { it.key },
                    ) { student ->
                        StudentRow(
                            student = student,
                            onClick = { onOpenStudent(student.key) },
                            testTag = "today-student-row-" + student.key,
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun TodayActionRow(
    action: PersonalTodayAction,
    onClick: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(role = Role.Button, onClick = onClick)
            .testTag("today-action-" + action.actionId)
            .padding(vertical = 14.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.Top,
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .padding(end = 12.dp),
        ) {
            Text(
                text = action.actionText,
                style = MaterialTheme.typography.titleMedium,
            )
            Text(
                text = action.studentDisplayName + " · " + subjectLabelForKey(action.subjectKey),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp),
            )
            Text(
                text = action.caseTitle,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 3.dp),
            )
        }
        Text(
            text = dueLabel(action.dueBucket, action.dueOn),
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
    HorizontalDivider()
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

@Composable
private fun SectionTitle(
    text: String,
    modifier: Modifier = Modifier,
) {
    Text(
        text = text,
        style = MaterialTheme.typography.titleMedium,
        modifier = modifier.padding(bottom = 8.dp),
    )
}

@Composable
private fun EmptyMessage(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.bodyLarge,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(vertical = 12.dp),
    )
}

@Composable
private fun StatusMessage(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.bodyMedium,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(vertical = 12.dp),
    )
}

@Composable
private fun StudentRow(
    student: StudentDirectoryItem,
    onClick: () -> Unit,
    testTag: String,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(
                role = Role.Button,
                onClick = onClick,
            )
            .testTag(testTag)
            .padding(vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(text = student.studentDisplayName, style = MaterialTheme.typography.titleMedium)
            Text(
                text = student.organizationName + " · " + student.subjectSummary,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp),
            )
        }
        Text(
            text = "查看",
            style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.primary,
        )
    }
    HorizontalDivider()
}

private fun dueLabel(
    bucket: ActionDueBucket,
    dueOn: java.time.LocalDate?,
): String = when (bucket) {
    ActionDueBucket.Overdue ->
        dueOn?.let { "逾期 · " + it.monthValue + "月" + it.dayOfMonth + "日" } ?: "逾期"

    ActionDueBucket.Today -> "今天"
    ActionDueBucket.Undated -> "待安排"
    ActionDueBucket.Future ->
        dueOn?.let { it.monthValue.toString() + "月" + it.dayOfMonth + "日" } ?: "之后"
}

private fun caseStateLabel(state: LearningCaseState): String = when (state) {
    LearningCaseState.New -> "新建"
    LearningCaseState.Confirmed -> "已确认"
    LearningCaseState.Intervening -> "跟进中"
    LearningCaseState.PendingVerification -> "待验证"
    LearningCaseState.Stable -> "稳定"
}

private fun studentStorageKey(
    organizationId: String,
    studentId: String,
): String = organizationId + ":" + studentId
